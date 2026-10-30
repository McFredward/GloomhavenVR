// The complete production offer/resolve/hold/release coordinator, measurement,
// clipping and layer-restoration kernels execute against this native/uGUI boundary.
// Native tracker cells and readiness values are borrowed unchanged, never rebuilt.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

internal static class Singleton<T> where T:class
{ internal static T Instance=null!;internal static bool IsInitialized=>Instance!=null; }
internal enum EReadyUpToggleStates { NotSet, Quests, CityEvents, Rewards, Retirement }
internal sealed class UIReadyToggle:MonoBehaviour
{ internal EReadyUpToggleStates readyUpToggleState;internal int ReadyCalls; }
internal sealed class UIReadyTrackerBar:MonoBehaviour { }
internal static class FFSNetwork { internal static bool IsOnline=true; }
namespace TMPro { internal sealed class TextMeshProUGUI:MonoBehaviour { } }
namespace UnityEngine
{
    internal static class TryGetBoundary
    { internal static bool TryGetComponent<T>(this Component source,out T value)where T:Component{value=source.GetComponent<T>()!;return value!=null;} }
}
namespace UnityEngine.UI
{
    internal sealed class HorizontalOrVerticalLayoutGroup:MonoBehaviour { internal float spacing; }
    internal sealed class GridLayoutGroup:MonoBehaviour { internal Vector2 spacing; }
    internal sealed class RectMask2D:MonoBehaviour { internal RectTransform rectTransform=>(RectTransform)transform; }
    internal sealed class Mask:MonoBehaviour { internal RectTransform rectTransform=>(RectTransform)transform; }
}
namespace GloomhavenVR.Core
{
    internal static class VRLog { internal static readonly List<string> Warnings=new();internal static void Warn(string _,string message)=>Warnings.Add(message);internal static void Info(string _,string message) { } }
}
namespace GloomhavenVR.WorldUI.MapRoom
{
    internal static class MapRoomDriver { internal static bool Active=true; }
    internal static class TransientFamilies { internal static int OfGraphic(Transform root,Transform limit,Dictionary<Transform,int> memo)=>0; }
    internal static partial class MapQuestReadyRoster
    {
        internal static UIWindow? NoticeOwner;
        internal static ReadyRosterSite Site=>_site;
        internal static GameObject? Bar=>_row;
        private static void ShowNotice(UIWindow owner)=>NoticeOwner=owner;
        private static void HideNotice()=>NoticeOwner=null;
        private static void Report(UIWindow? host,GameObject? confirm) { }
    }
    internal static partial class MapTravelConfirm
    {
        private const float MinVisibleAlpha=.02f;
        private static readonly List<Graphic> ContentGraphics=new();
        private static readonly Dictionary<Transform,int> TransientMemo=new();
        private static readonly Vector3[] CornerScratch=new Vector3[4],MaskCornerScratch=new Vector3[4];
    }
}
