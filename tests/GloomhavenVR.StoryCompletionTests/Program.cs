using System;
using GloomhavenVR.WorldUI;
using UnityEngine.UI;

internal static class Program
{
    private static int _assertions;

    private static void Check(bool value, string message)
    {
        ++_assertions;
        if (!value) throw new InvalidOperationException(message);
    }

    private static void Main()
    {
        Check(!NativeStoryWindow.IsStory(null) && !NativeStoryWindow.IsCompleted(null),
            "missing window never identifies a story");
        Check(!ModalFallback.Poll(null), "missing window never enters polling");
        var orphan = new UIWindow();
        Check(!NativeStoryWindow.IsStory(orphan), "uninitialized singleton never claims an unrelated window");
        var map = new UIWindow();
        var scenario = new UIWindow();
        Singleton<MapStoryController>.Instance = new MapStoryController { window = map };
        Singleton<StoryController>.Instance = new StoryController { window = scenario };
        foreach (UIWindow window in new[] { map, scenario })
        {
            Check(NativeStoryWindow.IsStory(window), "both native controllers identify their exact window");
            Check(!NativeStoryWindow.IsStory(new UIWindow { Parent = window }),
                "story descendants are not story windows");
            window.Parent = new UIWindow();
            Check(!NativeStoryWindow.IsStory(window.Parent), "story ancestors are not story windows");
            foreach (bool sticky in new[] { false, true })
            foreach (bool tracked in new[] { false, true })
            foreach (float alpha in new[] { 0f, 0.25f, 1f })
            {
                var panel = new WindowPanel { Window = window, Sticky = sticky };
                window.IsOpen = true;
                window.Alpha = alpha;
                Check(!NativeStoryWindow.IsCompleted(window), "live pages are never completed");
                Check(ModalFallback.Keep(panel, tracked) == (tracked || sticky),
                    "live story retains ordinary membership and stickiness");
                window.IsOpen = false; // Native final-page Hide has already run its callbacks.
                Check(NativeStoryWindow.IsCompleted(window),
                    "native closed state owns completion despite visible alpha");
                Check(!ModalFallback.Keep(panel, tracked),
                    "closed final story releases even with stale membership and positive alpha");
                Check(!ModalFallback.WouldReassert(panel),
                    "completed story cannot be made visible again");
                Check(!ModalFallback.Poll(window), "closed story alpha cannot reenter polling");
                Check(!ModalFallback.WouldConvert(window), "stale sampled story cannot reconvert after release");
                window.IsOpen = true; // A queued message may reuse the same instance immediately.
                Check(!NativeStoryWindow.IsCompleted(window)
                    && ModalFallback.Keep(panel, tracked) == (tracked || sticky),
                    "same-instance reopen retains the new live message");
                Check(ModalFallback.WouldReassert(panel), "live story keeps normal visibility support");
                Check(ModalFallback.Poll(window) && ModalFallback.WouldConvert(window),
                    "same-instance reopen restores polling and conversion");
            }
        }

        foreach (string role in new[] { "merchant", "temple", "character", "quest-log", "options" })
        foreach (float alpha in new[] { 0f, 0.25f, 1f })
        {
            var window = new UIWindow { Alpha = alpha };
            var panel = new WindowPanel { Window = window, Sticky = true };
            Check(!NativeStoryWindow.IsStory(window) && !NativeStoryWindow.IsCompleted(window),
                "ordinary destination and permanent panel keep their identity: " + role);
            Check(ModalFallback.Keep(panel, false),
                "ordinary destination and permanent panel retain parallel lifetime: " + role);
            Check(ModalFallback.WouldReassert(panel),
                "ordinary destination and permanent panel retain visibility support: " + role);
            Check(ModalFallback.Poll(window) && ModalFallback.WouldConvert(window),
                "ordinary destination and permanent panel retain poll and conversion admission: " + role);
        }
        Check(MandatoryDecisionTerms.IdentifiesTheWindow(MandatoryDecisionTerm.NativeStory),
            "native story classification spends completed stickiness");
        Check(!MandatoryDecisionTerms.IdentifiesTheWindow(MandatoryDecisionTerm.GameRefusesEscape),
            "escape refusal alone never spends destination stickiness");
        CheckFreshStoryAnchors(map, scenario);
        Console.WriteLine($"Story completion: {_assertions} runtime assertions passed.");
    }

    private static void CheckFreshStoryAnchors(UIWindow map, UIWindow scenario)
    {
        foreach (UIWindow story in new[] { map, scenario })
        {
            SharedWindowKind kind = ReferenceEquals(story, map)
                ? SharedWindowKind.MapStory : SharedWindowKind.ScenarioStory;
            ModalFallback.Converted.Clear();
            SharedWindows.Online = true;
            SharedWindows.CompositeHost = null;
            story.IsOpen = true;
            ModalFallback.SharedAnchorSpent.Add(kind);
            ModalFallback.SharedAnchorSpentWhy[kind] = "old story was dragged";
            ModalFallback.SharedAnchorSpent.Add(SharedWindowKind.RewardShowcase);
            ModalFallback.SharedAnchorSpentWhy[SharedWindowKind.RewardShowcase] = "reward is still held";
            // This method runs only on initial conversion, after the old frame was released.
            ModalFallback.FreshStoryAnchor(story);
            Check(!ModalFallback.SharedAnchorSpent.Contains(kind)
                && !ModalFallback.SharedAnchorSpentWhy.ContainsKey(kind),
                "fresh story reclaims its shared spawn seat after prior movement");
            Check(ModalFallback.SharedAnchorSpent.Contains(SharedWindowKind.RewardShowcase)
                && ModalFallback.SharedAnchorSpentWhy.ContainsKey(SharedWindowKind.RewardShowcase),
                "new story never rearms other shared windows");

            ModalFallback.SharedAnchorSpent.Add(kind);
            ModalFallback.SharedAnchorSpentWhy[kind] = "live story is held or peer-placed";
            var existing = new WindowPanel { Window = story };
            ModalFallback.Converted.Add(existing);
            ModalFallback.FreshStoryAnchor(story);
            Check(ModalFallback.SharedAnchorSpent.Contains(kind),
                "live story frame retains its spent anchor and manual pose");
            existing.Panel.IsAlive = false;
            ModalFallback.FreshStoryAnchor(story);
            Check(!ModalFallback.SharedAnchorSpent.Contains(kind),
                "destroyed predecessor cannot suppress new story shared placement");
            ModalFallback.Converted.Clear();

            foreach (bool online in new[] { false, true })
            {
                ModalFallback.SharedAnchorSpent.Add(kind);
                story.IsOpen = online ? false : true;
                SharedWindows.Online = online;
                ModalFallback.FreshStoryAnchor(story);
                Check(ModalFallback.SharedAnchorSpent.Contains(kind),
                    "private or completed story cannot rearm shared placement");
            }
        }

        map.IsOpen = true;
        SharedWindows.Online = true;
        SharedWindows.CompositeHost = new UIWindow { IsOpen = true };
        ModalFallback.SharedAnchorSpent.Add(SharedWindowKind.MapStory);
        ModalFallback.FreshStoryAnchor(map);
        ModalFallback.FreshStoryAnchor(SharedWindows.CompositeHost);
        Check(ModalFallback.SharedAnchorSpent.Contains(SharedWindowKind.MapStory),
            "composite handoff preserves its existing shared pose");
        SharedWindows.CompositeHost = null;
        ModalFallback.FreshStoryAnchor(new UIWindow { IsOpen = true });
        Check(ModalFallback.SharedAnchorSpent.Contains(SharedWindowKind.MapStory),
            "unrelated window cannot rearm native story placement");
    }
}

internal static class Singleton<T> where T : class
{
    internal static T Instance = null!;
    internal static bool IsInitialized => Instance != null;
}
internal sealed class MapStoryController { internal UIWindow window = null!; }
internal sealed class StoryController { internal UIWindow window = null!; }

namespace UnityEngine.UI
{
    internal sealed class UIWindow
    {
        internal bool IsOpen;
        internal float Alpha;
        internal bool IsVisible => Alpha > 0f;
        internal UIWindow? Parent;
    }
}

namespace GloomhavenVR.WorldUI
{
    internal sealed class WindowPanel
    {
        internal UIWindow Window = null!;
        internal readonly ConvertedPanel Panel = new();
        internal bool Sticky;
        internal bool UserClosing { get; set; }
        internal bool EmptyReleasePending { get; set; }
    }
    internal sealed class ConvertedPanel { internal bool IsAlive = true; }
    internal enum SharedWindowKind { None, MapStory, ScenarioStory, RewardShowcase }
    internal static class SharedWindows
    {
        internal static bool Online;
        internal static UIWindow? CompositeHost;
        internal static bool ParticipatesHere(SharedWindowKind kind) => Online && kind != SharedWindowKind.None;
        internal static SharedWindowKind KindOf(UIWindow window)
        {
            if (ReferenceEquals(Singleton<StoryController>.Instance?.window, window)) return SharedWindowKind.ScenarioStory;
            if (CompositeHost != null)
                return ReferenceEquals(CompositeHost, window) ? SharedWindowKind.MapStory : SharedWindowKind.None;
            return ReferenceEquals(Singleton<MapStoryController>.Instance?.window, window)
                ? SharedWindowKind.MapStory : SharedWindowKind.None;
        }
    }
}
