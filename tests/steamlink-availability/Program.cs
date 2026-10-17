using System;
using System.Collections.Generic;
namespace GloomhavenVR.Core
{
    internal static class FrameNativePassthrough
    { internal static bool Required; internal static int Status = 0; internal static bool IsAvailable = true; }
    internal static class MixedReality { internal static Entry SteamLinkNative = new(); }
    internal sealed class Entry { internal bool Value; }
    internal static class TickGuard
    { internal static void Run(string key, Action callback, string category) => callback(); }
}
namespace GloomhavenVR.Core.SteamLink
{
    internal static class SteamLinkConnection { internal static bool Available; }
    internal static partial class SteamLinkSceneRuntime { }
}
namespace GloomhavenVR.WorldUI
{
    internal static class ConfigCatalog
    { internal sealed class ConfigItem { internal string Section = "", Key = ""; } }
    internal static partial class VROptionsTab
    {
        private static readonly Func<object?, bool> Off = static v => v is bool b && !b;
        private static readonly List<Action> MixedRealityAvailabilityRefreshers = new();
        private static int _mixedRealityAvailabilitySignature = -1;
        private static bool? _steamLinkAvailable;
        private static bool IsOpen = true;
        private static int _rebuilds;
        private static bool MixedRealityCanEnable => true;
        private static bool HasItsOwnPage(ConfigCatalog.ConfigItem item) => false;
        private static bool IsShownForCurrentVariant(ConfigCatalog.ConfigItem item) => true;
        private static bool DependencyMet(ConfigCatalog.ConfigItem item) => item.Section != "MixedReality"
            || item.Key != "KeyColor" || KeyPredicate(Core.MixedReality.SteamLinkNative.Value);
        private static void Rebuild() { _rebuilds++; ClearMixedRealityAvailabilityRefreshers(); }
        internal static int CheckAll()
        {
            int assertions = 0;
            void Check(bool condition, string name)
            { assertions++; if (!condition) throw new Exception(name); }
            var native = new ConfigCatalog.ConfigItem { Section = "MixedReality", Key = "SteamLinkNative" };
            var key = new ConfigCatalog.ConfigItem { Section = "MixedReality", Key = "KeyColor" };
            var unrelated = new ConfigCatalog.ConfigItem { Section = "Cards", Key = "Enabled" };
            for (int i = 0; i != 8; i++)
            {
                bool required = (i & 1) != 0, available = (i & 2) != 0, preference = (i & 4) != 0;
                Core.FrameNativePassthrough.Required = required;
                Core.SteamLink.SteamLinkConnection.Available = available;
                Core.MixedReality.SteamLinkNative.Value = preference;
                Check(Core.SteamLink.SteamLinkSceneRuntime.Requested == (!required && available && preference),
                    "only proven Frame/vrlink plus preference can request a source");
                Check(IsRowVisible(native) == (!required && available), "native control requires current Frame/vrlink");
                Check(IsRowVisible(key) == (!required && (!available || !preference)),
                    "unsupported persisted-on preference must retain chromakey controls");
                Check(IsRowVisible(unrelated), "hardware gating never hides unrelated controls");
            }
            Core.FrameNativePassthrough.Required = false;
            Core.SteamLink.SteamLinkConnection.Available = false;
            ClearMixedRealityAvailabilityRefreshers();
            for (int i = 0; i != 100; i++) RefreshMixedRealityAvailability();
            Check(_rebuilds == 0, "stable pending capability does not rebuild");
            Core.SteamLink.SteamLinkConnection.Available = true;
            RefreshMixedRealityAvailability();
            Check(_rebuilds == 1, "resolved capability refreshes an already open pane with no registered controls");
            for (int i = 0; i != 100; i++) RefreshMixedRealityAvailability();
            Check(_rebuilds == 1, "stable available capability does not rebuild per frame");
            Core.SteamLink.SteamLinkConnection.Available = false;
            RefreshMixedRealityAvailability();
            Check(_rebuilds == 2, "disconnect removes native control and restores fallback once");
            IsOpen = false; Core.SteamLink.SteamLinkConnection.Available = true;
            RefreshMixedRealityAvailability();
            Check(_rebuilds == 2, "closed pane does not rebuild");
            return assertions;
        }
    }
}
internal static class Program
{
    private static void Main() => Console.WriteLine("PASS native availability integration: "
        + GloomhavenVR.WorldUI.VROptionsTab.CheckAll() + " assertions");
}
