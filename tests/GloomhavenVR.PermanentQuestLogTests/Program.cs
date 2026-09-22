using System;
using System.Collections.Generic;
using UnityEngine.UI;

namespace UnityEngine.UI
{
    internal sealed class UIWindow
    {
        internal bool QuestLog;
        internal bool PartyPanel;
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
    internal static class MapRoomDriver
    {
        internal static bool Active = true;
        internal static MapRenderer? ParchmentRenderer;
    }
    internal sealed class MapRenderer
    {
        internal bool enabled = true;
        internal MapObject gameObject = new();
    }
    internal sealed class MapObject { internal bool activeInHierarchy = true; }
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
        private static readonly HashSet<UIWindow> Floated = new();
        private static int _checks;
        private static bool IsQuestLogWindow(UIWindow? window) => window != null && window.QuestLog;
        private static bool IsMapRoomPermanent(UIWindow window) => window.PartyPanel || window.QuestLog;
        private static bool IsFloatedByUs(UIWindow window) => Floated.Contains(window);
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
            // New Guildmaster case: native-hidden before the first Show event, then an
            // ordinary dialog, actual confirmation/story, and return to browsing.
            MapRuleLibrary.Adventure.AdventureState.MapState.IsCampaign = false;
            MapRoom.MapRoomDriver.ParchmentRenderer = new MapRoom.MapRenderer();
            var original = new UIWindow { QuestLog = true, IsOpen = false };
            QuestManager.Instance = new QuestManager { questLog = new NativeQuestLog { Window = original } };
            Tick();
            Check(OpenWindows.Count == 1 && ReferenceEquals(OpenWindows[0], original),
                "Guildmaster must discover the original before any native Show event");
            CompletePermanentQuestLogReturn(original);
            Tick();
            Check(OpenWindows.Count == 1, "Guildmaster must retain its original after conversion");
            // The source policy classifies an ordinary Guildmaster HideOtherGUI dialog as
            // browsing; actual native confirmation and loadout still raise the curtain.
            foreach (bool campaign in new[] { false, true })
            foreach (bool hides in new[] { false, true })
            foreach (bool committed in new[] { false, true })
            foreach (bool loadout in new[] { false, true })
            foreach (bool journey in new[] { false, true })
            {
                bool expected = hides && (campaign || committed || loadout || journey);
                Check(MapStoryCurtainPolicy.HidesForMessage(campaign, hides, committed, loadout, journey) == expected,
                    "Story curtain must distinguish browsing from actual quest commitment");
            }
            StoryComposite.PointOfNoReturn = MapStoryCurtainPolicy.HidesForMessage(false, true, false, false, false);
            Tick();
            Check(OpenWindows.Count == 1, "Ordinary Guildmaster dialog keeps quest list visible");
            StoryComposite.PointOfNoReturn = MapStoryCurtainPolicy.HidesForMessage(false, true, true, false, false);
            FloatRefusalTable.Refused = true;
            Tick();
            Check(OpenWindows.Count == 0, "Accepted Guildmaster quest must retain the real story curtain");
            StoryComposite.PointOfNoReturn = false;
            FloatRefusalTable.Refused = false;
            Tick();
            Check(OpenWindows.Count == 1 && !original.IsOpen, "Guildmaster return must preserve native hidden state");
            MapRoom.MapRoomDriver.ParchmentRenderer.enabled = false;
            Check(!IsStandingGuildmasterQuestLog(original), "Invisible parchment must not gain permanent enrollment");
            Tick();
            Check(OpenWindows.Count == 0, "Hidden Guildmaster parchment must stop enrollment");
            MapRoom.MapRoomDriver.ParchmentRenderer.enabled = true;
            MapRoom.MapRoomDriver.ParchmentRenderer.gameObject.activeInHierarchy = false;
            Check(!IsStandingGuildmasterQuestLog(original), "Inactive map must not gain permanent enrollment");
            Tick();
            Check(OpenWindows.Count == 0, "Inactive Guildmaster parchment must stop enrollment");
            MapRoom.MapRoomDriver.ParchmentRenderer.gameObject.activeInHierarchy = true;
            MapRoom.MapRoomDriver.Active = false;
            Tick();
            Check(OpenWindows.Count == 0, "Guildmaster map exit must clear pending return");
            MapRoom.MapRoomDriver.Active = true;
            QuestManager.Instance = null;
            Tick();
            Check(OpenWindows.Count == 0, "Destroyed native manager cannot restore a stale quest list");
            TestPresentationReturn();
            Console.WriteLine($"Permanent quest log: {_checks} production-linked assertions passed.");
        }

        private static void TestPresentationReturn()
        {
            ResetPermanentQuestLog();
            MapRuleLibrary.Adventure.AdventureState.MapState.IsCampaign = true;
            foreach (bool flat in new[] { false, true })
            foreach (bool map in new[] { false, true })
            foreach (bool running in new[] { false, true })
                Check(MapRoomWindowReturn.IsPresentationSwitch(flat, map, running) == (flat && map && running),
                    "only a live flat-map presentation switch may preserve native windows");

            // Repeat beyond the catch-all's historical three-open fuse. Enrollment must not
            // depend on a new native Show or on that catch-all counter; the real original is used.
            for (int cycle = 0; cycle < 100; cycle++)
            {
                Floated.Clear();
                QuestManager.Instance = null;
                NewPartyDisplayUI.PartyDisplay = null;
                MapRoomWindowReturn.Arm();
                Tick();
                Check(OpenWindows.Count == 0, "missing native roots must wait without inventing widgets");
                var party = new UIWindow { PartyPanel = true, IsOpen = false };
                var quest = new UIWindow { QuestLog = true, IsOpen = false };
                NewPartyDisplayUI.PartyDisplay = new NativeQuestLog { Window = party };
                QuestManager.Instance = new QuestManager { questLog = new NativeQuestLog { Window = quest } };
                MapRuleLibrary.Adventure.AdventureState.MapState.HeadquartersState.PartyUIUnlocked = false;
                Tick();
                Check(OpenWindows.Count == 0, "native introductory character unlock must remain authoritative");
                MapRuleLibrary.Adventure.AdventureState.MapState.HeadquartersState.PartyUIUnlocked = true;
                StoryComposite.PointOfNoReturn = true;
                Tick();
                Check(OpenWindows.Count == 0, "return must wait for actual quest story and loadout");
                StoryComposite.PointOfNoReturn = false;
                FloatRefusalTable.Refused = true;
                Tick();
                Check(OpenWindows.Count == 0, "journey and other temporary refusal must retain authority");
                FloatRefusalTable.Refused = false;
                WorldUIConfig.ConversionActive = false;
                Tick();
                Check(OpenWindows.Count == 0, "2D presentation must not enroll VR windows");
                WorldUIConfig.ConversionActive = true;
                MapRoom.MapRoomDriver.Active = false;
                Tick();
                Check(OpenWindows.Count == 0, "a room not yet ready cannot return windows");
                MapRoom.MapRoomDriver.Active = true;
                MapRoom.MapRoomDriver.ParchmentRenderer!.enabled = false;
                Tick();
                Check(OpenWindows.Count == 0, "hidden map must not return its windows");
                MapRoom.MapRoomDriver.ParchmentRenderer.enabled = true;
                Tick();
                Check(OpenWindows.Count == 2 && OpenWindows.Contains(party) && OpenWindows.Contains(quest),
                    "return must enroll both original native-hidden permanent windows");
                Tick();
                Check(OpenWindows.Count == 2, "requests survive conversion delay without duplicate enrollment");
                Check(!party.IsOpen && !quest.IsOpen, "return must not call native Show or mutate open state");
                Floated.Add(party);
                Tick();
                Check(OpenWindows.Count == 1 && OpenWindows.Contains(quest),
                    "successful party conversion leaves the pending quest independently recoverable");
                Floated.Add(quest);
                Tick();
                Check(OpenWindows.Count == 0, "successful conversion consumes both requests");
                Floated.Clear();
                Tick();
                Check(OpenWindows.Count == 0, "later native flow must not trigger an unsolicited reentry");
                MapRoomWindowReturn.Arm();
                MapRoomWindowReturn.Reset();
                Tick();
                Check(OpenWindows.Count == 0, "scene teardown cancels pending restoration");
            }
        }
    }
}

namespace MapRuleLibrary.Adventure
{
    internal sealed class MapState { internal bool IsCampaign = true; internal HeadquartersState HeadquartersState = new(); }
    internal sealed class HeadquartersState { internal bool PartyUIUnlocked; }
    internal static class AdventureState { internal static MapState MapState = new(); }
}
internal sealed class QuestManager
{
    internal static QuestManager? Instance;
    internal NativeQuestLog? questLog;
}
internal sealed class NativeQuestLog
{
    internal UIWindow? Window;
    internal T? GetComponent<T>() where T : class => Window as T;
}
internal static class NewPartyDisplayUI { internal static NativeQuestLog? PartyDisplay; }
