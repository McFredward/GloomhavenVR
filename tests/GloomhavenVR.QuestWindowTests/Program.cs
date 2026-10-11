using System;
using System.Collections.Generic;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine.UI;

// Native subject discovery is tested by map-quest-selection-runtime. This fixture supplies that
// boundary and runs the complete production shared-window lookup and floated-host accessor.
// The two native roots deliberately collide by ID and reverse their order in the float set.
namespace UnityEngine { internal class NamespaceMarker { } }
namespace UnityEngine.UI
{
    internal enum UIWindowID { QuestPopup, None }
    internal sealed class UIWindow
    {
        internal UIWindowID ID;
        internal object gameObject = new();
        internal bool IsOpen = true;
        internal UIQuestPopup? Popup = new();
        internal bool? HasSubject;
        internal T? GetComponent<T>() where T : class => Popup as T;
    }
}
internal static class FFSNetwork
{
    internal static bool IsOnline { get; set; }
    internal static bool IsHost { get; set; }
    internal static bool IsClient => IsOnline && !IsHost;
}
namespace FFSNet
{
    internal static class PlayerRegistry { internal static List<object> AllPlayers { get; } = new(); }
}
internal static class Singleton<T> where T : class
{
    internal static bool IsInitialized => false;
    internal static T Instance => throw new InvalidOperationException("Unrelated native singleton queried");
}
internal sealed class StoryController { internal UIWindow window => throw new NotSupportedException(); }
internal sealed class UIQuestPopup { }
internal sealed class MapStoryController { internal UIWindow window => throw new NotSupportedException(); }
internal sealed class UIEventPanel
{
    internal object gameObject => throw new NotSupportedException();
    internal T GetComponent<T>() => throw new NotSupportedException();
}
namespace GloomhavenVR.Core
{
    internal static class VRLog { internal static void Info(string scope, string message) { } }
}
namespace GloomhavenVR.WorldUI.MapRoom
{
    internal static class MapRoomDriver { internal static bool Active { get; set; } = true; }
    internal static class NativeMapQuestSelection
    {
        internal static UIWindow? ConfirmationWindow { get; set; }
        internal static bool IsConfirmationWindow(UIWindow? window) => window != null && ReferenceEquals(window, ConfirmationWindow);
        internal static bool TryHasSubject(UIWindow? window, out bool hasSubject)
        {
            hasSubject = window?.HasSubject == true;
            return window?.HasSubject != null;
        }
    }
    internal static class MapLocationInteractor
    {
        internal static bool SelectionCleared { get; set; }
        internal static bool QuestSelectionCleared(out string why) { why = "fixture decision"; return SelectionCleared; }
    }
}
namespace GloomhavenVR.WorldUI
{
    internal sealed class GrabbableModal { internal bool IsGrabbed { get; set; } }
    internal static class RewardShowcase { internal static UIWindow? Window => null; }
    internal static class NativeVideoWindow
    {
        internal static UIWindow? Window => null;
        internal static bool TryGetGrab(out GrabbableModal? grab) { grab = null; return false; }
    }
    internal static class SharedWindowIdentity { internal static UIWindow? MapStoryHost => null; }
    internal static partial class ModalFallback
    {
        internal sealed class Panel
        {
            internal bool IsAlive = true;
            internal object? HostGo = new();
        }
        internal sealed class WindowPanel
        {
            internal UIWindow? Window;
            internal Panel Panel = new();
            internal GrabbableModal? Grab = new();
            internal bool UserClosing;
            internal bool Sticky = true;
        }
        internal static readonly List<WindowPanel> Converted = new();
        internal static bool SubjectSpent(WindowPanel panel) => StickinessSpentByClearedQuestSelection(panel, out _);
        internal static bool TryGetGrabFor(UIWindow? window, out GrabbableModal? grab)
        {
            grab = null;
            foreach (WindowPanel panel in Converted)
                if (panel.Grab != null && ReferenceEquals(panel.Window, window)) { grab = panel.Grab; return true; }
            return false;
        }
        internal static bool TryGetGrabById(UIWindowID id, out GrabbableModal? grab)
        {
            grab = null;
            foreach (WindowPanel panel in Converted)
                if (panel.Window?.ID == id) { grab = panel.Grab; return grab != null; }
            return false;
        }
    }
}
internal static class Program
{
    private static int _checks;
    private static void Check(bool value, string why)
    {
        _checks++;
        if (!value) throw new InvalidOperationException(why);
    }
    private static void Main()
    {
        foreach (bool host in new[] { false, true })
        foreach (bool reversed in new[] { false, true })
        foreach (UIWindowID id in new[] { UIWindowID.QuestPopup, UIWindowID.None })
        {
            FFSNetwork.IsOnline = true;
            FFSNetwork.IsHost = host;
            MapRoomDriver.Active = true;
            var current = new ModalFallback.WindowPanel { Window = new UIWindow { ID = id } };
            var hover = new ModalFallback.WindowPanel { Window = new UIWindow { ID = UIWindowID.QuestPopup } };
            ModalFallback.Converted.Clear();
            ModalFallback.Converted.Add(reversed ? current : hover);
            ModalFallback.Converted.Add(reversed ? hover : current);
            NativeMapQuestSelection.ConfirmationWindow = current.Window;
            Check(SharedWindows.KindOf(current.Window) == SharedWindowKind.QuestConfirm, "Native current confirmation must be shared even without an authored ID");
            Check(SharedWindows.KindOf(hover.Window) == SharedWindowKind.None, "Unrelated hover sharing the same ID must remain private");
            Check(ReferenceEquals(SharedWindows.WindowOf(SharedWindowKind.QuestConfirm), current.Window), "Shared kind resolves the current native subject");
            Check(SharedWindows.TryGetGrab(SharedWindowKind.QuestConfirm, out var grab) && ReferenceEquals(grab, current.Grab), "Pose publication must use the current native grab, not first authored ID");
            Check(ReferenceEquals(ModalFallback.FloatedQuestConfirmationWindow(), current.Window), "Accept parking and shared pose name the same native instance");
            Check(SharedWindows.IsShared(current.Window), "Native confirmation participates with either host role");
            current.UserClosing = true;
            Check(ModalFallback.FloatedQuestConfirmationWindow() == null, "Closing current confirmation must never select the other same-ID view");
            current.UserClosing = false;
            current.Panel.IsAlive = false;
            Check(ModalFallback.FloatedQuestConfirmationWindow() == null, "Dead native conversion cannot host Accept");
            current.Panel.IsAlive = true;
            current.Panel.HostGo = null;
            Check(ModalFallback.FloatedQuestConfirmationWindow() == null, "Missing converted host remains a presentation fallback");
            current.Panel.HostGo = new();
            NativeMapQuestSelection.ConfirmationWindow = hover.Window;
            Check(SharedWindows.TryGetGrab(SharedWindowKind.QuestConfirm, out grab) && ReferenceEquals(grab, hover.Grab), "Native subject swap immediately retargets the shared grab");
            NativeMapQuestSelection.ConfirmationWindow = null;
            Check(ModalFallback.FloatedQuestConfirmationWindow() == null, "Native cancellation does not revive a stale authored-ID panel");
            Check(!SharedWindows.TryGetGrab(SharedWindowKind.QuestConfirm, out _), "Native cancellation releases shared subject lookup");
            FFSNetwork.IsOnline = false;
            NativeMapQuestSelection.ConfirmationWindow = current.Window;
            Check(!SharedWindows.IsShared(current.Window), "Offline confirmation retains private placement");
            FFSNetwork.IsOnline = true;
            MapRoomDriver.Active = false;
            Check(!SharedWindows.IsShared(current.Window), "Scenario/2D map cannot participate in 3D quest pose arbitration");
            current.Window!.HasSubject = true;
            current.Window.IsOpen = false;
            MapLocationInteractor.SelectionCleared = false;
            Check(!ModalFallback.SubjectSpent(current), "A temporary hide retains the current native subject");
            hover.Window!.HasSubject = false;
            hover.Window.IsOpen = false;
            Check(ModalFallback.SubjectSpent(hover), "Ending the separate hover subject must release its closed sticky popup while the accepted quest remains");
            hover.Window.IsOpen = true;
            Check(!ModalFallback.SubjectSpent(hover), "Native close animation precedes subject release");
            hover.Window.IsOpen = false;
            hover.Window.HasSubject = null;
            Check(!ModalFallback.SubjectSpent(hover), "Unavailable native subject evidence cannot invent cancellation");
            MapLocationInteractor.SelectionCleared = true;
            Check(ModalFallback.SubjectSpent(hover), "Settled VR deselection remains a fallback for unavailable native subject evidence");
            hover.Window.Popup = null;
            Check(!ModalFallback.SubjectSpent(hover), "Ordinary sticky map destinations are not quest subjects");
        }
        Console.WriteLine($"Quest window: {_checks} production-linked assertions passed.");
    }
}
