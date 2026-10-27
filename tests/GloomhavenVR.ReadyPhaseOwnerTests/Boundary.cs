// Bounded native/uGUI boundary. Full production TickCore, Park, Unpark and
// TryFindFloatedGroupLeader execute here; rendering/Photon outcomes are not modeled.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

internal static class Singleton<T> where T:class
{ internal static T Instance=null!;internal static bool IsInitialized=>Instance!=null; }
internal enum EReadyUpToggleStates { NotSet, Quests, CityEvents, Rewards, Retirement, TownRecords }
internal sealed class UIReadyToggle:MonoBehaviour
{
    internal EReadyUpToggleStates readyUpToggleState;
    internal UIWindow window=null!;
    internal bool IsVisible=>window.IsOpen;
    internal int Requests;
    internal bool Interactable;
    internal void Press() { if (Interactable&&window.IsOpen) ++Requests; }
}
internal sealed class UIQuestPopup:MonoBehaviour { }
internal sealed class UILoadoutManager:MonoBehaviour
{ internal bool IsOpen;internal Button? confirmationButton; }
internal static class FFSNetwork { internal static bool IsOnline; }
namespace UnityEngine
{
    internal static class NonGenericComponentBoundary
    {
        internal static Component? GetComponent(this Component source,Type type)
        { foreach(Component item in source.gameObject.Components)if(!item.Destroyed&&type.IsInstanceOfType(item))return item;return null; }
    }
}
namespace UnityEngine.UI { public sealed class Button:MonoBehaviour { } }
namespace GloomhavenVR.Core
{
    internal static class VRLog
    { internal static readonly List<string> Warnings=new();internal static void Warn(string _,string text)=>Warnings.Add(text);internal static void Info(string _,string text) { } }
}
namespace GloomhavenVR.WorldUI.MapRoom
{ internal static class MapRoomDriver { internal static bool Active; } }
namespace GloomhavenVR.WorldUI
{
    internal static partial class LoadoutConfirmPark
    {
        private const string Scope="MapRoom";
        internal static UILoadoutManager? NativeManager;
        internal static UIWindow? NativeHost;
        internal static bool _standDown,_standDownWarned,_parkedIsReadyToggle,_posed,_parkLogged;
        private static bool _homeRecorded;
        private static GameObject? _homeOwner;
        internal static GameObject? _parked;
        internal static UIWindow? _host;
        internal static Transform? _home;
        private static int _homeIndex;
        private static Vector2 _homeAnchorMin,_homeAnchorMax,_homePivot,_homeAnchoredPos;
        private static Quaternion _homeRotation;
        private static Vector3 _homeScale;
        private static LayoutElement? _addedIgnore;
        internal static bool Claimed;
        private static readonly LayerBoundary Layers=new();
        private sealed class LayerBoundary { internal void Restore() { } }
        private static UILoadoutManager? Manager()=>NativeManager;
        private static UIWindow? CharacterWindow()=>NativeHost;
        private static bool FloatedByMod(UIWindow target)=>ModalFallback.Converted.Exists(w=>ReferenceEquals(w.Window,target)&&w.Panel.IsAlive&&!w.UserClosing);
        private static void ResetReach() { }
        private static void ReportReach(UILoadoutManager manager,UIWindow? loadout) { }
        private static void DropClaim(string why)=>Claimed=false;
        private static void TickClaim(GameObject? control,bool readyToggle,bool parked)=>Claimed=control!=null&&readyToggle&&parked;
        private static bool LayerDrifted(GameObject control,int layer)=>false;
        private static void WriteLayers(int layer,Transform root)=>root.gameObject.layer=layer;
        private static void ApplyPose(UIWindow host,GameObject control)=>_posed=true;
        private static void ResetAnchor(string why) { }
        internal static void Reconcile()=>TickCore();
        internal static void Fresh()
        {
            Unpark("fixture reset");NativeManager=null;NativeHost=null;Claimed=false;_homeRecorded=false;_homeOwner=null;_home=null;
            _standDown=false;_standDownWarned=false;
        }
    }
    internal sealed class ConvertedPanel
    {
        internal RectTransform Target=null!;
        internal Transform? NativeHome;
        internal int Sibling;
        internal Vector2 AnchorMin,AnchorMax,Pivot,Position;
        internal Vector3 Scale;
        internal Quaternion Rotation;
        internal GameObject HostGo=new("Old standalone ready host");
        internal bool IsAlive=true;
        internal ConvertedPanel(UIWindow target,bool reparent)
        {
            Target=(RectTransform)target.transform;NativeHome=Target.parent;Sibling=Target.GetSiblingIndex();
            AnchorMin=Target.anchorMin;AnchorMax=Target.anchorMax;Pivot=Target.pivot;Position=Target.anchoredPosition;Scale=Target.localScale;Rotation=Target.localRotation;
            if(reparent){Target.SetParent(HostGo.transform,false);Target.localScale=new(12,12,12);}
        }
    }
    internal sealed class Grab { internal void Destroy() { } }
    internal static class CanvasConversion
    {
        internal static readonly List<ConvertedPanel> ActivePanels=new();
        internal static void Release(ConvertedPanel panel)
        {
            ActivePanels.Remove(panel);panel.Target.SetParent(panel.NativeHome,false);panel.Target.SetSiblingIndex(panel.Sibling);
            panel.Target.anchorMin=panel.AnchorMin;panel.Target.anchorMax=panel.AnchorMax;panel.Target.pivot=panel.Pivot;
            panel.Target.anchoredPosition=panel.Position;panel.Target.localScale=panel.Scale;panel.Target.localRotation=panel.Rotation;
            panel.IsAlive=false;UnityEngine.Object.Destroy(panel.HostGo);
        }
    }
    internal static class WindowMaterialise
    {
        internal static readonly Dictionary<ConvertedPanel,Action> Pending=new();
        internal static void DropPreRoll(ConvertedPanel panel,string why) { }
        internal static void Cancel(ConvertedPanel panel,string why) { if(Pending.Remove(panel,out Action? callback))callback(); }
    }
    internal static partial class ModalFallback
    {
        internal sealed class WindowPanel
        { internal UIWindow Window=null!;internal ConvertedPanel Panel=null!;internal Grab? Grab;internal bool UserClosing,EmptyReleasePending; }
        internal static readonly List<WindowPanel> Converted=new();
        private static void ReleasePreConvertHide(UIWindow window,string why) { }
        private static void ReleaseScreenBind(UIWindow window,string why) { }
        internal static bool Group(UIWindow window,out UIWindow? leader)=>TryFindFloatedGroupLeader(window,out leader,out _);
        internal static ConvertedPanel Float(UIWindow window,bool move=false,bool dying=false,bool closing=false)
        {
            var panel=new ConvertedPanel(window,move);CanvasConversion.ActivePanels.Add(panel);
            if(dying)WindowMaterialise.Pending[panel]=()=>CanvasConversion.Release(panel);
            else Converted.Add(new WindowPanel{Window=window,Panel=panel,Grab=new(),UserClosing=closing});
            return panel;
        }
        internal static void Fresh(){Converted.Clear();CanvasConversion.ActivePanels.Clear();WindowMaterialise.Pending.Clear();}
    }
}
