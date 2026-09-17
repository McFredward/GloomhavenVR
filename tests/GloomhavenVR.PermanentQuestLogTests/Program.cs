using System;
using System.Collections.Generic;
using UnityEngine.UI;

namespace UnityEngine.UI
{
    internal sealed class UIWindow
    {
        internal bool QuestLog;
        internal bool IsOpen;
        internal bool Destroyed;
        public static bool operator ==(UIWindow? a, UIWindow? b) =>
            ReferenceEquals(a, b) || (ReferenceEquals(a, null) && b!.Destroyed)
            || (ReferenceEquals(b, null) && a!.Destroyed);
        public static bool operator !=(UIWindow? a, UIWindow? b) => !(a == b);
        public override bool Equals(object? other) => ReferenceEquals(this, other);
        public override int GetHashCode() => base.GetHashCode();
    }
}

namespace GloomhavenVR.WorldUI.MapRoom
{
    internal static class MapRoomDriver { internal static bool Active = true; }
}
namespace GloomhavenVR.WorldUI
{
    internal static class StoryComposite { internal static bool PointOfNoReturn; }
    internal static class WorldUIConfig { internal static bool ConversionActive = true; }
    internal static class FloatRefusalTable
    {
        internal static bool Refused;
        internal static bool Refuses(UIWindow window) => Refused;
    }
    internal static partial class ModalFallback
    {
        private static readonly List<UIWindow> OpenWindows = new();
        private static int _checks;
        private static bool IsQuestLogWindow(UIWindow window) => window.QuestLog;
        private static void AddPollWindow(UIWindow window)
        {
            if (!FloatRefusalTable.Refuses(window) && !OpenWindows.Contains(window))
                OpenWindows.Add(window);
        }
        private static void Check(bool value, string reason)
        {
            _checks++;
            if (!value) throw new Exception(reason);
        }
        private static void Tick()
        {
            OpenWindows.Clear();
            TickPermanentQuestLog();
        }
        private static void Main()
        {
            foreach (bool nativeOpen in new[] { false, true })
            {
                var log = new UIWindow { QuestLog = true, IsOpen = nativeOpen };
                var unrelated = new UIWindow { IsOpen = true };
                ResetPermanentQuestLog();
                RememberWithdrawnQuestLog(unrelated);
                Tick();
                Check(OpenWindows.Count == 0, "unrelated windows must not resurrect");
                RememberWithdrawnQuestLog(log);
                StoryComposite.PointOfNoReturn = true;
                Tick();
                Check(OpenWindows.Count == 0, "story and loadout exclusion must remain");
                StoryComposite.PointOfNoReturn = false;
                FloatRefusalTable.Refused = true;
                Tick();
                Check(OpenWindows.Count == 0, "refusal remains authoritative");
                FloatRefusalTable.Refused = false;
                WorldUIConfig.ConversionActive = false;
                Tick();
                Check(OpenWindows.Count == 0, "flat presentation must not enroll VR windows");
                WorldUIConfig.ConversionActive = true;
                CompletePermanentQuestLogReturn(unrelated);
                Tick();
                Check(OpenWindows.Count == 1 && ReferenceEquals(OpenWindows[0], log),
                    "native closed quest log must return after curtain");
                Check(log.IsOpen == nativeOpen, "native state must remain untouched");
                Tick();
                Check(OpenWindows.Count == 1, "conversion delays retain one pending return");
                CompletePermanentQuestLogReturn(log);
                Tick();
                Check(OpenWindows.Count == 0, "successful conversion consumes return");
                RememberWithdrawnQuestLog(log);
                log.Destroyed = true;
                Tick();
                Check(OpenWindows.Count == 0, "destroyed original is never resurrected");
                log.Destroyed = false;
                Tick();
                Check(OpenWindows.Count == 0, "destroyed subject cannot leak into next scene");
                RememberWithdrawnQuestLog(log);
                MapRoom.MapRoomDriver.Active = false;
                Tick();
                Check(OpenWindows.Count == 0, "map exit must clear pending return");
                MapRoom.MapRoomDriver.Active = true;
                Tick();
                Check(OpenWindows.Count == 0, "new map must not inherit previous log");
                RememberWithdrawnQuestLog(log);
                ResetPermanentQuestLog();
                Tick();
                Check(OpenWindows.Count == 0, "explicit room shutdown clears pending return");
            }
            Console.WriteLine($"Permanent quest log: {_checks} production-linked assertions passed.");
        }
    }
}
