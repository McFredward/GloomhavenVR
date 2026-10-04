namespace GloomhavenVR.Core
{
    // Existing bridge's configuration/platform policy is verified by the Quest
    // platform suite. This seam selects its result for the actual row filter.
    internal static class QuestStandalonePlatform { internal static bool Enabled; }
}
namespace GloomhavenVR.WorldUI
{
    internal static class ConfigCatalog
    {
        internal sealed class ConfigItem
        {
            internal readonly string Section, Key;
            internal readonly object PersistedValue = new();
            internal bool OwnPage;
            internal bool VariantVisible = true, DependencySatisfied = true;
            internal ConfigItem(string section, string key) { Section = section; Key = key; }
        }
    }
    internal static partial class VROptionsTab
    {
        internal static bool Visible(ConfigCatalog.ConfigItem item) => IsRowVisible(item);
        private static bool HasItsOwnPage(ConfigCatalog.ConfigItem item) => item.OwnPage;
        private static bool IsShownForCurrentVariant(ConfigCatalog.ConfigItem item) => item.VariantVisible;
        private static bool DependencyMet(ConfigCatalog.ConfigItem item) => item.DependencySatisfied;
    }
}
