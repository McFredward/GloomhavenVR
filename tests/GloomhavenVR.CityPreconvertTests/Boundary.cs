// Only the native/capture APIs beyond the focused conversion boundary are modeled.
// Production helper and conversion entry statements execute unchanged.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Core
{ internal static class VRLog { internal static bool WantsDebug;internal static void Warn(string scope,string message) { } } }
namespace GloomhavenVR.WorldUI.MapRoom
{
    internal static class MapRoomDriver { internal static bool Active; }
    internal static class MapCityEventReadyUp { internal static UIWindow? Window; }
    internal enum ReadyRosterSite { Quest,EnterDungeon,CityEvent }
    internal static class MapTravelConfirm
    {
        internal static void Reconcile(UIWindow? quest)
        {
            WorldUI.ModalFallback.Steps.Add("release-quest");
            if(!ReferenceEquals(quest,WorldUI.ModalFallback.Quest))throw new InvalidOperationException("exact quest owner must reach reconciliation");
            WorldUI.ModalFallback.QuestHolding=false;
        }
    }
    internal static class MapQuestReadyRoster
    {
        internal static void Tick(ReadyRosterSite site,UIWindow? host,GameObject? confirm,bool confirmPosed)
        {
            WorldUI.ModalFallback.Steps.Add("adopt-row");
            if(WorldUI.ModalFallback.QuestHolding||WorldUI.ModalFallback.LoadoutHolding)
                throw new InvalidOperationException("old parkers must release before native row adoption");
            if(site!=ReadyRosterSite.CityEvent||!ReferenceEquals(host,MapCityEventReadyUp.Window)
                ||!ReferenceEquals(confirm,host?.gameObject)||!confirmPosed)
                throw new InvalidOperationException("city row must use exact native proposal and control");
            WorldUI.ModalFallback.RowAdopted=true;
        }
    }
}
namespace GloomhavenVR.WorldUI
{
    internal sealed class ConvertedPanel { }
    internal sealed class GrabbableModal { }
    internal static class ConfirmationOnOwner { internal static bool IsOwned(UIWindow window)=>false; }
    // This city readiness fixture contains no native character-card inventory.
    // Inventory ownership/retirement executes in the separate native level-up proof.
    internal static class LevelUpComposite { internal static bool IsOwnedInventory(UIWindow window)=>false; }
    internal static class LoadoutConfirmPark
    { internal static void Tick(){ModalFallback.Steps.Add("release-loadout");ModalFallback.LoadoutHolding=false;} }
    internal static partial class ModalFallback
    {
        private const int ModalConvertTimingLimit=64;
        private static int _modalConvertTimingReports;
        private sealed class WindowPanel { }
        internal static readonly List<string> Steps=new();
        internal static UIWindow? Quest;
        internal static bool QuestHolding,LoadoutHolding,RowAdopted;
        private static UIWindow? FloatedQuestConfirmationWindow()=>Quest;
        private static void ReleasePreConvertHide(UIWindow window,string why)=>Steps.Add("release-hidden-render");
        private static void ReleaseScreenBind(UIWindow window,string why)=>Steps.Add("release-screen-bind");
        private static void FirstContentSnapshot(UIWindow window,RectTransform rect)
        {
            Steps.Add("first-content-snapshot");
            if(ReferenceEquals(MapRoom.MapCityEventReadyUp.Window,window)&&MapRoom.MapRoomDriver.Active&&window.IsOpen
                &&(!RowAdopted||QuestHolding||LoadoutHolding))
                throw new InvalidOperationException("city conversion cannot snapshot incomplete native content");
        }
        internal static void Prepare(UIWindow window)=>PrepareCityReadinessForConversion(window);
        internal static bool ConvertEntry(UIWindow window)=>TryConvertWindow(window);
        internal static void Fresh()
        {Steps.Clear();Quest=null;QuestHolding=LoadoutHolding=true;RowAdopted=false;}
    }
}
