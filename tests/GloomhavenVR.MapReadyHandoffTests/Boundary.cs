// uGUI hierarchy and frame boundary, not a headset/Photon simulation. Actual Park,
// Reconcile/ReconcileCore, Unpark, release handoff and modal lifetime expressions
// are compiled from the production source by the runner. Native ready-window
// visibility is read from the original public UIWindow state throughout.
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

internal static class Singleton<T> where T:class
{ internal static T Instance=null!; internal static bool IsInitialized=>Instance!=null; }
internal enum EReadyUpToggleStates { NotSet, CityEvents, Quests, Rewards, Retirement }
internal sealed class UIReadyToggle:MonoBehaviour
{
    internal EReadyUpToggleStates readyUpToggleState;
    internal UIWindow window=null!;
    internal bool IsVisible=>window.IsOpen;
    internal int Requests;
    internal Action? OnPressed;
    internal void Initialize(EReadyUpToggleStates state, bool participant)
    { readyUpToggleState=state; window.Hide(); OnPressed=participant?()=>++Requests:null; }
    internal void Press() { if (window.IsOpen) OnPressed?.Invoke(); }
}
internal sealed class AdventureMapUIManager
{ internal readonly GameObject travelOptions=new("Offline options"); internal Component? travelButton; internal bool IsLocked; }
internal sealed class UIIntroductionManager
{ internal readonly Layout LayoutGroup=new(); internal sealed class Layout { internal UIWindow? window; } }
internal static class FFSNetwork { internal static bool IsOnline; }
namespace GLOO.Introduction { }
namespace GloomhavenVR.Core
{
    internal static class VRLog { internal static void Warn(string _,string message)=>Messages.Add(message);internal static void Info(string _,string message) { } internal static List<string> Messages=new(); }
}
namespace GloomhavenVR.WorldUI.MapRoom
{
    internal static class MapRoomDriver { internal static bool Active; }
    internal static class ReadyToggleParkClaim { internal static bool Claimed; }
    internal static class MapQuestReadyUp
    {
        internal static void TickPendingClientPrompt() { }
        internal static void TickClientConfirmReveal(UIReadyToggle? toggle,UIWindow? host) { }
        internal static void TickClaim(GameObject? current,bool visible,GameObject? parked,UIWindow? host,bool hasWindow,bool down)
        { ReadyToggleParkClaim.Claimed=!down&&current!=null&&visible&&ReferenceEquals(current,parked)&&host!=null; }
        internal static void Reset()=>ReadyToggleParkClaim.Claimed=false;
    }
    internal enum ReadyRosterSite { Quest }
    internal static class MapQuestReadyRoster { internal static void Tick(ReadyRosterSite _,UIWindow? host,GameObject? toggle,bool posed) { } internal static void Reset() { } }
    internal static class MapInputGate { internal static bool IsBlockedBy(AdventureMapUIManager manager)=>manager.IsLocked; }
    internal static partial class MapTravelConfirm
    {
        private const string Scope="MapRoom";
        internal static bool _standDown,_parkStandDown,_parkedIsReadyToggle,_posed,_wasActive,_revealForcedLogged,_reported,_parkWarned;
        private static bool _placementLogged,_homeRecorded;
        private static GameObject? _homeOwner;
        internal static GameObject? _parked;
        internal static UIWindow? _host;
        internal static Transform? _optionsHome;
        private static int _optionsHomeIndex;
        private static Vector2 _homeAnchorMin,_homeAnchorMax,_homePivot,_homeAnchoredPos,_loggedOffset,_loggedDials;
        private static Quaternion _homeLocalRotation;
        private static Vector3 _homeLocalScale;
        private static float _activeSince,_placementLoggedAt,_anchorNextRefreshAt,_watchNextReportAt;
        private static string _layoutOwner="";
        private static CanvasGroup? _hold;
        private static LayoutElement? _layoutIgnore;
        internal static AdventureMapUIManager NativeManager=new();
        private static readonly FieldInfo? _travelOptions=typeof(AdventureMapUIManager).GetField("travelOptions",BindingFlags.Instance|BindingFlags.NonPublic);
        private static readonly FieldInfo? _travelButton=typeof(AdventureMapUIManager).GetField("travelButton",BindingFlags.Instance|BindingFlags.NonPublic);
        private static readonly FieldInfo? _readyToggleState=typeof(UIReadyToggle).GetField("readyUpToggleState",BindingFlags.Instance|BindingFlags.NonPublic);
        private static bool EnsureReflection()=>true;
        private static AdventureMapUIManager? Manager()=>NativeManager;
        private static void NoteOnlineConfirmMissing(UIWindow window) { }
        private static void WarnCannotPark(string message) { _parkStandDown=true;GloomhavenVR.Core.VRLog.Warn(Scope,message); }
        private static void EnsureHold(GameObject options)=>_hold=options.GetComponent<CanvasGroup>()??options.AddComponent<CanvasGroup>();
        private static void SetHidden(bool hide) { if(_hold!=null) { _hold.alpha=hide?0:1;_hold.blocksRaycasts=!hide; } }
        private static void ReleaseHold() { if(_hold!=null) { _hold.alpha=1;_hold.blocksRaycasts=true;_hold=null; } }
        private static void EnsureLayoutIgnore(GameObject options) { _layoutIgnore=options.GetComponent<LayoutElement>()??options.AddComponent<LayoutElement>();_layoutIgnore.ignoreLayout=true; }
        private static void ReleaseLayoutIgnore() { if(_layoutIgnore!=null) _layoutIgnore.ignoreLayout=false;_layoutIgnore=null; }
        private static string DescribeLayoutOwner(RectTransform win,GameObject options)=>"fixture native layout boundary";
        private static void ResetDriftWatch() { }
        private static void ResetAnchor(string why) { }
        private static void ApplyPose(RectTransform rect,RectTransform win,out float x,out float y,out float z)
        { x=y=z=0;rect.anchoredPosition=new(0,-100);_posed=true; }
        private static void PlaceButton(UIWindow win,GameObject options,AdventureMapUIManager? mgr)=>ApplyPose((RectTransform)options.transform,(RectTransform)win.transform,out _,out _,out _);
        private static void TickVisibility(bool active) { }
        internal static void Fresh()
        {
            Reset();_standDown=false;_parkStandDown=false;_homeRecorded=false;_homeOwner=null;_optionsHome=null;_reported=false;NativeManager=new();
            GloomhavenVR.Core.VRLog.Messages.Clear();
        }
    }
}
namespace GloomhavenVR.WorldUI
{
    internal sealed class ConvertedPanel
    {
        internal UIWindow Window=null!;
        internal RectTransform Target=null!;
        internal Transform? NativeHome;
        internal int Sibling;
        internal Vector2 AnchorMin,AnchorMax,Pivot,Size,Position;
        internal Vector3 Scale;
        internal Quaternion Rotation;
        internal GameObject HostGo=new("Old modal host");
        internal bool IsAlive=true;
        internal ConvertedPanel(UIWindow window)
        {
            Window=window;Target=(RectTransform)window.transform;NativeHome=Target.parent;Sibling=Target.GetSiblingIndex();
            AnchorMin=Target.anchorMin;AnchorMax=Target.anchorMax;Pivot=Target.pivot;Size=Target.sizeDelta;Position=Target.anchoredPosition;Scale=Target.localScale;Rotation=Target.localRotation;
            Target.SetParent(HostGo.transform,false);Target.localScale=new(17,17,17);Target.anchoredPosition=new(900,800);
        }
    }
    internal sealed class Grab { internal void Destroy() { } }
    internal static class CanvasConversion
    {
        internal static readonly List<ConvertedPanel> ActivePanels=new();
        internal static int Releases;
        internal static void Release(ConvertedPanel panel)
        {
            Releases++;ActivePanels.Remove(panel);panel.Target.SetParent(panel.NativeHome,false);panel.Target.SetSiblingIndex(panel.Sibling);
            panel.Target.anchorMin=panel.AnchorMin;panel.Target.anchorMax=panel.AnchorMax;panel.Target.pivot=panel.Pivot;panel.Target.sizeDelta=panel.Size;
            panel.Target.anchoredPosition=panel.Position;panel.Target.localScale=panel.Scale;panel.Target.localRotation=panel.Rotation;
            panel.IsAlive=false;UnityEngine.Object.Destroy(panel.HostGo);
        }
    }
    internal static class WindowMaterialise
    {
        internal static readonly Dictionary<ConvertedPanel,Action> Pending=new();
        internal static void DropPreRoll(ConvertedPanel panel,string why) { }
        internal static void Cancel(ConvertedPanel panel,string why) { if(Pending.Remove(panel,out Action? callback)) callback(); }
    }
    internal static class NativeStoryWindow { internal static bool IsCompleted(UIWindow? window)=>false; }
    internal static class MenuWindowFamily { internal static bool IsGameOwnedMenu(UIWindow window)=>false; }
    internal static partial class ModalFallback
    {
        internal sealed class WindowPanel
        { internal UIWindow Window=null!;internal ConvertedPanel Panel=null!;internal Grab? Grab;internal bool UserClosing,Sticky,EmptyReleasePending; }
        internal static readonly List<WindowPanel> Converted=new();
        internal static readonly List<UIWindow> OpenWindows=new();
        internal static bool MapRoomParallel(UIWindow window)=>GloomhavenVR.WorldUI.MapRoom.MapRoomDriver.Active;
        private static bool ContainsWindow(List<UIWindow> list,UIWindow window)=>list.Contains(window);
        private static bool ScriptedLevelMessageActive(UIWindow? window)=>false;
        private static void ReleasePreConvertHide(UIWindow window,string why) { }
        private static void ReleaseScreenBind(UIWindow window,string why) { }
        internal static ConvertedPanel Float(UIWindow window,bool dying=false,bool userClosing=false)
        {
            var panel=new ConvertedPanel(window);CanvasConversion.ActivePanels.Add(panel);
            if(dying) WindowMaterialise.Pending[panel]=()=>CanvasConversion.Release(panel);
            else Converted.Add(new WindowPanel{Window=window,Panel=panel,UserClosing=userClosing,Sticky=StickyFor(window)});
            return panel;
        }
        internal static void TickFloats()
        {
            for(int i=Converted.Count-1;i>=0;--i)
            {
                WindowPanel wp=Converted[i];
                // The real catch-all rebuild excludes the claimed ready target from
                // OpenWindows before its release pass. Preserve that frame boundary.
                if(GloomhavenVR.WorldUI.MapRoom.ReadyToggleParkClaim.Claimed
                    && ReferenceEquals(wp.Window.gameObject,GloomhavenVR.WorldUI.MapRoom.MapTravelConfirm._parked))
                { DropOwnFloat(wp.Window);OpenWindows.Remove(wp.Window); }
                if(Keep(wp)) continue;
                Converted.RemoveAt(i);CanvasConversion.Release(wp.Panel);
            }
        }
        internal static void Fresh()
        { Converted.Clear();OpenWindows.Clear();CanvasConversion.ActivePanels.Clear();CanvasConversion.Releases=0;WindowMaterialise.Pending.Clear(); }
    }
}
