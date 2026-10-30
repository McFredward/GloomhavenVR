// Native proposal/lifecycle state is supplied at this boundary. The actual shared
// KindOf/WindowOf/IsShared and RemoteMapStory EventPanel/EncounterKey methods run
// unchanged. This fixture does not simulate consent, Photon, or headset pixels.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

internal static class Singleton<T> where T:class
{ internal static T Instance=null!;internal static bool IsInitialized=>Instance!=null; }
internal sealed class StoryController { internal UIWindow? window; }
internal sealed class MapStoryController { internal UIWindow? window; }
internal sealed class UIReadyToggle:MonoBehaviour
{ internal string Caption="";internal int ReadyCalls;internal void ReadyUp()=>++ReadyCalls; }
internal sealed class UIEventPanel:MonoBehaviour
{ internal MapRuleLibrary.YML.Events.CRoadEvent? eventData;internal string LocalisedTitle=""; }
internal static class FFSNetwork { internal static bool IsOnline; }
namespace MapRuleLibrary.YML.Events { internal sealed class CRoadEvent { internal string? ID; } }
namespace GloomhavenVR.WorldUI.MapRoom
{
    internal static class MapRoomDriver { internal static bool Active; }
    internal static class MapCityEventReadyUp { internal static UIWindow? Window;internal static uint ContentKey; }
    internal static class NativeMapQuestSelection
    { internal static UIWindow? ConfirmationWindow;internal static bool IsConfirmationWindow(UIWindow window)=>ReferenceEquals(window,ConfirmationWindow); }
}
namespace GloomhavenVR.WorldUI
{
    internal static class RewardShowcase { internal static UIWindow? Window; }
    internal static class NativeVideoWindow { internal static UIWindow? Window; }
    internal static class SharedWindowIdentity { internal static UIWindow? MapStoryHost; }
    internal sealed class GrabbableModal { }
    internal static partial class SharedWindows { internal static bool SessionIsOnline()=>FFSNetwork.IsOnline; }
    internal static class ModalFallback
    {
        internal static readonly Dictionary<UIWindow,GrabbableModal?> Floated=new();
        internal static bool TryGetGrabFor(UIWindow? window,out GrabbableModal? grab)
        { if(window!=null&&Floated.TryGetValue(window,out grab))return true;grab=null;return false; }
    }
}
namespace GloomhavenVR.Net
{
    internal static partial class RemoteMapStory
    {
        internal static UIWindow? OfferedWindow()=>EventPanel();
        internal static uint KeyOf(UIWindow? window)=>EncounterKey(window);
    }
}
