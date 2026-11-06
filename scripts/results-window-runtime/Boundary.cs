// Explicit ports: game/bootstrap/network registry, native vote acknowledgements,
// conversion allocation and native GUI animation clocks. Actual Unity geometry,
// graphic callbacks and new production presentation/ownership helpers execute.
#pragma warning disable CS0649, CS0414
using System;
using System.Collections.Generic;
using GLOOM;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
public static class Singleton<T> where T:class { public static T Instance=null!;public static bool IsInitialized=>Instance!=null; }
public enum EReadyUpToggleStates { NotSet,Quests }
public enum ActionProcessorStateType { ProcessFreely,Halted }
public enum ActionPhaseType { ScenarioEnded }
public class NetworkControllable { }
public class NetworkPlayer { public bool IsParticipant=true; }
public delegate void ControllerChangedEvent(NetworkControllable c,NetworkPlayer o,NetworkPlayer n);
public static class ControllableRegistry { public static ControllerChangedEvent? OnControllerChanged; }
public static class FFSNetwork { public static bool IsOnline=true,IsClient; }
public static class InputManager { public static bool GamePadInUse; }
public static class ActionProcessor { public static int Writes;public static void SetState(ActionProcessorStateType s,ActionPhaseType p=default){Writes++;} }
namespace GLOOM { public static class LocalizationManager { public static string GetTranslation(string key)=>key; } }
namespace FFSNet { public static class PlayerRegistry { public static NetworkPlayer MyPlayer=new(); } }
namespace UnityEngine.UI { public class UIWindow:MonoBehaviour { public bool IsOpen=true;public bool IsVisible=>IsOpen;public readonly UnityEvent onHidden=new();internal int Shows,Hides;public void Show(){Shows++;IsOpen=true;var c=GetComponent<CanvasGroup>();if(c!=null)c.alpha=1;}public void Hide(){Hides++;onHidden.Invoke();IsOpen=false;var c=GetComponent<CanvasGroup>();if(c!=null)c.alpha=0;} } }
public class UIResultsManager:MonoBehaviour { public UIWindow myWindow=null!; }
public class GUIAnimator:MonoBehaviour { public bool IsPlaying; }
public class HeaderHighlightText:MonoBehaviour { public GUIAnimator highlightAnimator=null!; }
public class ControllerArea { public string Id="native-results"; }
public partial class UINewAdventureResultsManager:UIResultsManager {
 public HeaderHighlightText header=null!;public GameObject resultsPanel=null!,statsPanel=null!,buttonsContainer=null!;public GUIAnimator showStatsAnimation=null!;public ControllerArea controllerArea=new();
 public void BeginNativeReady(Action callback)=>ShowReadyUpToggles("GUI_RESULT_WIN_EXIT",callback,false);
 private void OnControllerChanged(NetworkControllable c,NetworkPlayer o,NetworkPlayer n){}
 private void ShowClientWaitForHostMessage(){Singleton<HelpBox>.Instance.ShowTranslated(LocalizationManager.GetTranslation("Consoles/GUI_WAIT_FOR_HOST_TIP"),LocalizationManager.GetTranslation("Consoles/GUI_MULTIPLAYER_TIP_TITLE"));}
 private void HideResultOptions(){}
}
public class HelpBox:MonoBehaviour {
 public sealed class HelpInfo { public string keybaordLines="",keyboardTitle=""; }
 public UIWindow myWindow=null!;public HelpInfo? m_HelpInfo;
 public void ShowTranslated(string text,string title){m_HelpInfo=new HelpInfo{keybaordLines=text,keyboardTitle=title};myWindow.Show();}
 public void Hide()=>myWindow.Hide();
}
public class UIReadyToggle:MonoBehaviour {
 public enum EReadyUpType { Participant,Player }
 public EReadyUpToggleStates readyUpToggleState;public EReadyUpType ReadyUpType;
 public UIWindow Window=null!;public Toggle Toggle=null!;public bool IsVisible=>Window.IsOpen;
 public int ReadyCalls,InteractableWrites;public UnityAction? AllReady;
 private UnityAction? _ready,_unready;
 public void Initialize(bool show,UnityAction onReady,UnityAction onUnready,UnityAction onAllPlayersReady,UnityAction<NetworkPlayer,bool> onReadiedPlayersChanged,Action? onShortPressed,Func<bool>? canReadyUp,string readyTextLoc,string unreadyTextLoc,string readyAudioItem,bool bringToFront,UnityAction<bool>? onStartProgress,UnityAction<bool>? onEndProgress,bool validateReadyUpOnPlayerLeft,EReadyUpType readyUpType,EReadyUpToggleStates readyUpToggleState,string controllerAreaAllowed){
  _ready=onReady;_unready=onUnready;AllReady=onAllPlayersReady;ReadyUpType=readyUpType;this.readyUpToggleState=readyUpToggleState;
  if(show)Window.Show();Toggle.onValueChanged.RemoveAllListeners();Toggle.onValueChanged.AddListener(ReadyUp);
 }
 public void ReadyUp(bool toggledOn){ReadyCalls++;if(toggledOn)_ready?.Invoke();else _unready?.Invoke();}
 public void SetInteractable(bool interactable){InteractableWrites++;Toggle.interactable=interactable;}
 public void SetLabelTextGamepad(string text){}
}
public class UIScenarioMultiplayerController { public int Writes;public void UpdateReadyPlayer(NetworkPlayer p,bool ready){Writes++;}public void HideReadyTracker(){}public void ShowReadyTracker(bool bringToFront,bool showExhausted){} }
namespace GloomhavenVR.Core { internal static class VRSession { internal static bool IsRunning=true; }internal static class VRLog {internal static void Info(string s,string t){}internal static void Warn(string s,string t){}internal static void Alert(string s,string t){}internal static void Note(string s,string t){}internal static void Debug(string s,string t){}internal static bool WantsDebug=>false; } }
namespace GloomhavenVR.Core.Events { internal static class VRModeStateMachine { internal static bool TableInFrontOfPlayer=true; } }
namespace GloomhavenVR.WorldUI {
 internal static class WorldUIConfig { internal static bool ConversionActive=true; }
 internal static class FlatScreen { internal static bool ManualScreenActive; }
 internal sealed class ConvertedPanel {
  internal RectTransform Target=null!,HostRect=null!;internal GameObject HostGo=null!;internal bool IsAlive=true;
  internal bool RenderHidden,OwnerRenderHidden,FitEnabled;internal RectTransform? FitContentRoot;internal Canvas HostCanvas=null!;internal readonly List<Transform> ExtraRenderRoots=new();
  internal Transform? NativeHome;internal Vector3 Position,Scale;internal Quaternion Rotation;internal Vector2 Min,Max,Pivot,Size;internal int Index;
 }
 internal static class CanvasConversion {
  internal static readonly List<ConvertedPanel> ActivePanels=new();internal static int Transfers;
  internal static void TransferSeatedSubtree(RectTransform root,ConvertedPanel? destination){Transfers++;}
  internal static void Release(ConvertedPanel p){
   p.Target.SetParent(p.NativeHome,false);p.Target.anchorMin=p.Min;p.Target.anchorMax=p.Max;p.Target.pivot=p.Pivot;p.Target.sizeDelta=p.Size;p.Target.anchoredPosition3D=p.Position;p.Target.localScale=p.Scale;p.Target.localRotation=p.Rotation;
   if(p.NativeHome!=null)p.Target.SetSiblingIndex(p.Index);p.IsAlive=false;ActivePanels.Remove(p);UnityEngine.Object.DestroyImmediate(p.HostGo);
  }
 }
 internal static class ConfirmationOnOwner { internal static bool ReleaseForExternalOwner(UIWindow w)=>true; }
 internal static class WindowMaterialise { internal static void DropPreRoll(ConvertedPanel p,string s){}internal static void Cancel(ConvertedPanel p,string s){} }
 internal static partial class ModalFallback {
  internal sealed class Grab { internal void Destroy(){} }
  internal sealed class WindowPanel { internal UIWindow Window=null!;internal ConvertedPanel Panel=null!;internal bool UserClosing;internal Grab? Grab; }
  internal static readonly List<WindowPanel> Converted=new();
  internal static void Unblackout(UIWindow w,string why){}
  internal static void ReleasePreConvertHide(UIWindow w,string why){}
  internal static void ReleaseScreenBind(UIWindow w,string why){}
 }
 internal static class NativeVideoWindow { internal static UIWindow? Window; }
 internal static class RewardShowcase { internal static UIWindow? Window; }
 internal static class SharedWindowIdentity { internal static UIWindow? MapStoryHost; }
 internal static partial class SharedWindows { private static bool SessionIsOnline()=>FFSNetwork.IsOnline; }
}
namespace GloomhavenVR.WorldUI.MapRoom {
 internal static class ReadyToggleParkClaim { internal static bool Claimed;internal static GameObject? ClaimedObject; }
 internal static class MapRoomDriver { internal static bool Active; }
 internal static class MapCityEventReadyUp { internal static UIWindow? Window; }
 internal static class NativeMapQuestSelection { internal static UIWindow? ConfirmationWindow;internal static bool IsConfirmationWindow(UIWindow w)=>ReferenceEquals(w,ConfirmationWindow); }
}
public class StoryController { public UIWindow? window; }
public class MapStoryController { public UIWindow? window; }
public class UIEventPanel:MonoBehaviour { }
