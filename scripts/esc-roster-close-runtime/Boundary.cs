using System;
using System.Collections.Generic;
using UnityEngine.UI;
internal enum UIWindowID { None, ESCMenu, Options, OptionsSubmenu, ViceOptionsSubmenu, CompendiumPanel, MultiplayerFriendList, MutiplayerPlayerPicker, MutiplayerHeroAssignPanel, HelpBox }
namespace UnityEngine.UI {
 internal sealed class UIWindow {
  internal string name="native"; internal UIWindowID ID; internal bool IsOpen=true, Mandatory;
  internal int Hides, Escapes; internal Action? OnHidden;
  internal bool Escape(){Escapes++;Hide();return true;}
  internal void Hide(){if(!IsOpen)return;Hides++;IsOpen=false;OnHidden?.Invoke();}
 }
}
namespace GloomhavenVR.Core { internal static class VRLog { internal static void Info(string s,string t){} internal static void Error(string s,string t){} } }
namespace GloomhavenVR.WorldUI {
 internal static class MessageWindowContinuation { internal static bool TryClose(UIWindow w)=>false; }
 internal static class ConfirmationOnOwner { internal static void BeforeUserWindowClose(UIWindow w){} }
 internal static class TownServiceTutorialPatches { internal static object? CaptureConvertedMerchantClose(UIWindow w)=>null;internal static void CompleteConvertedMerchantClose(UIWindow w,object? c){} }
 internal static partial class MenuWindowFamily { private static bool IsModOwned(UIWindow? w)=>false; }
 internal static partial class ModalFallback {
  internal sealed class Group { internal float alpha=1;internal bool blocksRaycasts=true,interactable=true; }
  internal sealed class WindowPanel { internal UIWindow Window=null!;internal bool Sticky,UserClosing;internal Group WindowCanvasGroup=new(); }
  internal static readonly List<WindowPanel> Converted=new(); internal static int Rescues;
  private static bool IsMapRoomPermanent(UIWindow w)=>false;
  private static string MapRoomPermanentReason(UIWindow w)=>"not in this fixture";
  private static bool IsMandatoryDecision(UIWindow w,out string reason){reason="native required";return w.Mandatory;}
  private static void RescueForMandatoryDecision(UIWindow w,string r,float t,string origin){Rescues++;}
  private static void ResetEscMenuToggleGroup(UIWindow w){}
 }
}
namespace GloomhavenVR.WorldUI.MapRoom {
 internal static class MapLocationInteractor { internal static bool HandleSharedQuestClose(UIWindow w)=>false; }
 internal static class MapCityEventReadyUp { internal static UIWindow? Window; }
 internal static class GuildmasterDestinations { internal static bool IsDestination(UIWindow w)=>false;internal static void LeaveMode(UIWindow w,string s){} }
 internal readonly struct TownWindowCloseScope:IDisposable { internal static TownWindowCloseScope Enter(bool d)=>new();public void Dispose(){} }
}
