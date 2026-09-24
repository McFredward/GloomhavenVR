using System;
using System.Collections.Generic;
public enum UIWindowID { None, EventsPanel }
public class UIEventPanel { }
public class UICampaignRewardWindow { }
public class UIRewardsManager { }
public class UIGuildmasterAdventureRewardsManager { }
public class ItemCardPicker { }
public class TakeDamagePanel { }
public class UILevelUpWindow { }
public class UIUnlockLocationFlowManager { }
public class UICharacterCreatorWindow { }
public class ConfirmationBox { }
public static class Singleton<T> where T : class
{
    public static T Instance = null!;
    public static bool IsInitialized => Instance != null;
}
public class StoryController { public UnityEngine.UI.UIWindow window = null!; }
public class MapStoryController { public UnityEngine.UI.UIWindow window = null!; }
namespace UnityEngine.UI
{
    public sealed class UIWindow
    {
        public enum EscapeKeyAction { None, Hide, Skip }
        public EscapeKeyAction escapeKeyAction = EscapeKeyAction.Hide;
        public string name = "Native window";
        public UIWindowID ID;
        public bool IsOpen = true, Permanent, Introduction, Destination, LevelMessage;
        public int Hides, Escapes;
        public object? Identity;
        public T? GetComponent<T>() where T : class => Identity as T;
        // Native UIWindow.Escape: None refuses; Skip succeeds without hiding; Hide hides.
        public bool Escape() { Escapes++; if (escapeKeyAction == EscapeKeyAction.None) return false;
            if (escapeKeyAction == EscapeKeyAction.Hide) Hide(); return true; }
        public void Hide() { Hides++; IsOpen = false; }
    }
}
namespace GloomhavenVR.Core
{
    public static class VRLog
    {
        public static readonly List<string> Lines = new();
        public static void Info(string scope, string text) => Lines.Add(text);
        public static void Note(string scope, string text) => Lines.Add(text);
        public static void Error(string scope, string text) => Lines.Add(text);
    }
}
namespace GloomhavenVR.WorldUI
{
    using UnityEngine.UI;
    // Semantic message continuation is exercised with its production helper in the
    // separate message-continuation suite. These subjects have no such controller.
    internal static class MessageWindowContinuation
    {
        internal static bool TryClose(UIWindow window) => false;
    }
    public static class FlatScreen
    {
        public static bool RescueScreenActive;
        public static string LastReason = "";
        public static int Requests;
        public static void RequestRescueScreen(string owner, string reason) { RescueScreenActive = true; LastReason = reason; Requests++; }
        public static void ReleaseRescueScreen(string reason) { RescueScreenActive = false; }
    }
    public static class MenuWindowFamily { public const string VROptionsWindowName = "VR Options"; }
    internal static partial class ModalFallback
    {
        internal sealed class CanvasGroup { public float alpha = 1f; public bool blocksRaycasts = true, interactable = true; }
        internal sealed class WindowPanel { public bool UserClosing; public CanvasGroup WindowCanvasGroup = new(); }
        internal static WindowPanel Panel = new();
        internal static int MenuResets;
        private static WindowPanel FindPanel(UIWindow window) => Panel;
        private static bool IsMapRoomPermanent(UIWindow window) => window.Permanent;
        private static string MapRoomPermanentReason(UIWindow window) => "permanent map window";
        private static bool IsIntroductionWindow(UIWindow? window) => window?.Introduction == true;
        private static bool IsLevelMessageWindow(UIWindow window) => window.LevelMessage;
        private static void ResetEscMenuToggleGroup(UIWindow window) { MenuResets++; }
        internal static void TickRescueForTest() => TickMandatoryRescue();
        internal static void ResetForTest() { _rescueWindow = null; Panel = new(); MenuResets = 0; }
    }
}
namespace GloomhavenVR.WorldUI.MapRoom
{
    using UnityEngine.UI;
    public static class MapRoomDriver { public static bool Active = true; }
    public static class GuildmasterDestinations
    {
        public static int LeaveCalls, Exits;
        public static bool IsDestination(UIWindow window) => window.Destination;
        public static void LeaveMode(UIWindow window, string source)
        {
            LeaveCalls++;
            if (MapRoomDriver.Active && window.Destination) { Exits++; window.Hide(); }
        }
    }
}
