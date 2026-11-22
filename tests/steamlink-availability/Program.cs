using System;

namespace GloomhavenVR.Core
{
    internal static class FrameNativePassthrough
    {
        internal static bool Standalone;
        internal static bool Required;
        internal static int Status;
        internal static bool IsAvailable;
    }

    internal static class TickGuard
    {
        internal static void Run(string key, Action callback, string category) => callback();
    }
}

namespace GloomhavenVR.Core.SteamLink
{
    internal static class SteamLinkConnection { internal static bool Available; }
}

namespace GloomhavenVR.WorldUI
{
    internal static class ConfigCatalog
    {
        internal sealed class ConfigItem { internal string Section = "MixedReality", Key = ""; }
    }

    internal static partial class VROptionsTab
    {
        private static bool IsOpen = true;
        private static int _rebuilds;
        private static bool HasItsOwnPage(ConfigCatalog.ConfigItem item) => false;
        private static bool IsShownForCurrentVariant(ConfigCatalog.ConfigItem item) => true;
        private static bool DependencyMet(ConfigCatalog.ConfigItem item) => true;

        private static void Rebuild()
        {
            _rebuilds++;
            ClearMixedRealityAvailabilityRefreshers();
            CaptureMixedRealityRowVisibility();
        }

        internal static int CheckAll()
        {
            int assertions = 0;
            void Check(bool condition, string name)
            {
                assertions++;
                if (!condition) throw new Exception(name);
            }

            var companion = new ConfigCatalog.ConfigItem { Key = "SteamLinkCompanion" };
            var key = new ConfigCatalog.ConfigItem { Key = "KeyColor" };
            var unrelated = new ConfigCatalog.ConfigItem { Section = "Cards", Key = "Enabled" };
            var retired = new[]
            {
                new ConfigCatalog.ConfigItem { Key = "SteamLinkNative" },
                new ConfigCatalog.ConfigItem { Key = "SteamLinkEdgeQuality" },
                new ConfigCatalog.ConfigItem { Key = "SteamLinkEncodeWidth" }
            };
            for (int i = 0; i != 8; i++)
            {
                bool standalone = (i & 1) != 0, required = (i & 2) != 0, linked = (i & 4) != 0;
                Core.FrameNativePassthrough.Standalone = standalone;
                Core.FrameNativePassthrough.Required = required || standalone;
                Core.SteamLink.SteamLinkConnection.Available = linked;
                Check(IsRowVisible(companion) == (!standalone && linked),
                    "legacy control is limited to identified Frame streaming");
                Check(IsRowVisible(key) == (!required && !standalone && !linked),
                    "chroma colour is reserved for generic PC streaming");
                Check(IsRowVisible(unrelated), "hardware gating never hides unrelated controls");
                foreach (var old in retired)
                    Check(!IsRowVisible(old), "retired companion and transmission options stay hidden");
            }

            Core.FrameNativePassthrough.Standalone = false;
            Core.FrameNativePassthrough.Required = false;
            Core.SteamLink.SteamLinkConnection.Available = false;
            ClearMixedRealityAvailabilityRefreshers();
            for (int i = 0; i != 100; i++) RefreshMixedRealityAvailability();
            Check(_rebuilds == 0, "stable pending capability does not rebuild");
            Core.SteamLink.SteamLinkConnection.Available = true;
            RefreshMixedRealityAvailability();
            Check(_rebuilds == 1, "identified connection updates an already open pane");
            for (int i = 0; i != 100; i++) RefreshMixedRealityAvailability();
            Check(_rebuilds == 1, "stable available capability does not rebuild per frame");
            Core.FrameNativePassthrough.Required = true;
            RefreshMixedRealityAvailability();
            Check(_rebuilds == 1, "official or companion selection keeps the same available rows for Frame streaming");
            Core.FrameNativePassthrough.Required = false;
            Core.SteamLink.SteamLinkConnection.Available = false;
            RefreshMixedRealityAvailability();
            Check(_rebuilds == 2, "disconnect removes the companion control and restores the colour once");

            ClearMixedRealityAvailabilityRefreshers();
            CaptureMixedRealityRowVisibility();
            Core.FrameNativePassthrough.Required = true;
            Core.FrameNativePassthrough.IsAvailable = true;
            Core.FrameNativePassthrough.Status = 4;
            RefreshMixedRealityAvailability();
            Check(_rebuilds == 3, "first tick sees capability change since building rows");
            Check(!IsRowVisible(key), "official support alone removes the obsolete chroma colour");
            Check(!IsRowVisible(companion), "official support alone does not expose companion");
            IsOpen = false;
            Core.FrameNativePassthrough.Required = false;
            Core.SteamLink.SteamLinkConnection.Available = true;
            RefreshMixedRealityAvailability();
            Check(_rebuilds == 3, "closed pane does not rebuild");
            return assertions;
        }
    }
}

internal static class Program
{
    private static void Main() => Console.WriteLine("PASS native availability integration: "
        + GloomhavenVR.WorldUI.VROptionsTab.CheckAll() + " assertions");
}
