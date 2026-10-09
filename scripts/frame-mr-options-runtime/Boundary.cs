using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// OpenXR lifecycle/capability and configuration persistence are tested separately.
// This fixture varies those explicit inputs and executes production UI policy,
// callbacks, tint and native tooltip attachment with actual Unity/uGUI/TMP objects.
namespace GloomhavenVR.Core
{
    internal enum FrameNativePassthroughStatus { Checking, Unsupported, QueryFailed, ActivationFailed, Available }
    internal static class FrameNativePassthrough
    {
        internal static bool Required { get; set; }
        internal static bool IsAvailable { get; set; }
        internal static FrameNativePassthroughStatus Status { get; set; }
    }
    internal static class QuestStandalonePlatform { internal static bool Enabled => false; }
    internal enum SkyStyle { Default, Cellar, SwampNight, OffBlack }
    internal sealed class BoolEntry
    {
        internal bool Value { get; set; }
        internal object BoxedValue { get => Value; set => Value = (bool)value; }
    }
    internal sealed class SkyEntry { internal SkyStyle Value = SkyStyle.Default; }
    internal static class MixedReality { internal static BoolEntry Enabled = new(); }
    internal static class SkyAlternative { internal static SkyEntry Style = new(); }
    internal static class Loc
    {
        internal static bool German;
        internal static string Mod(string key) => OriginalMrLabels.All.TryGetValue(key, out string[] text)
            ? text[German ? 1 : 0] : key;
        internal static string? ConfigHelpForPlayers(string section, string key) => section == "MixedReality" && key == "Enabled"
            ? "Replace the surroundings with a solid chroma-key color for compatible streaming passthrough software." : null;
        internal static string? ModHelpForPlayers(string key) => null;
    }
    internal static class VRLog
    {
        internal static bool WantsDebug => false;
        internal static void Warn(string area, string message) => throw new InvalidOperationException(message);
        internal static void Debug(string area, string message) { }
    }
    internal static class TickGuard { internal static void Run(string name, Action action, string area) => action(); }
    internal static class WallFadeTuning
    {
        internal static float RescanIntervalSeconds => 4;
        internal static float EffectiveEvalIntervalSeconds => 1;
    }
}
namespace GloomhavenVR.WorldUI
{
    // The Quest-only filter's behavior has its separate production platform suite.
    internal static class QuestOptionVisibility
    { internal static bool IsOffered(string section, string key, bool standalone) => true; }
    internal static class ConfigCatalog
    {
        internal sealed class ConfigItem
        {
            internal string Section = "MixedReality", Key = "Enabled";
            internal bool NeedsRestart => false;
            internal BoolEntry Entry = MixedReality.Enabled;
        }
        internal static void ToggleBool(ConfigItem item) => item.Entry.Value = !item.Entry.Value;
        internal static string Hint(ConfigItem item) => "fixture";
    }
    internal static partial class VROptionsTab
    {
        internal static bool IsOpen = true;
        private static GameObject _toggleTemplate = null!;
        private static readonly GameObject _dropdownControl = new("unused explicit dropdown donor");
        private static readonly List<GameObject> Rows = new();
        private static readonly List<(TMP_Text, Func<string>)> ValueLabels = new();
        private static readonly List<(Slider, ConfigCatalog.ConfigItem, int)> Sliders = new();
        private const float HintWidth = 420f;
        private const float HoverTintFadeSeconds = .06f;
        private static GameObject StampRow(GameObject template, Transform parent, out TMP_Text? title, out Transform? option)
        {
            GameObject row = UnityEngine.Object.Instantiate(template, parent, false);
            row.SetActive(true);
            Rows.Add(row); title = row.transform.Find("Title").GetComponent<TMP_Text>();
            option = row.transform.Find("Option"); return row;
        }
        private static T? PlaceControl<T>(Transform? option, GameObject template) where T : Component
        {
            if (option == null) return null;
            var toggle = option.GetComponent<Toggle>();
            if (toggle != null) UnityEngine.Object.DestroyImmediate(toggle);
            return option.gameObject.AddComponent<T>();
        }
        private static string Caption(ConfigCatalog.ConfigItem item, string? caption) => caption ?? item.Key;
        private static void ApplyOptionCaption(TMP_Text title) { }
        private static void IndentDependent(TMP_Text title, ConfigCatalog.ConfigItem item) { }
        private static void ProbeCaptionFit(TMP_Text title, string key) { }
        private static TMP_Text? ExistingValueLabel(GameObject row, TMP_Text? title) => null;
        private static bool HasItsOwnPage(ConfigCatalog.ConfigItem item) => false;
        private static bool IsShownForCurrentVariant(ConfigCatalog.ConfigItem item) => true;
        private static bool DependencyMet(ConfigCatalog.ConfigItem item) => true;
        internal static bool KeyColorVisible => IsRowVisible(new ConfigCatalog.ConfigItem { Key = "KeyColor" });
        private static bool SelectsAVariant(ConfigCatalog.ConfigItem item) => false;
        private static bool IsDependencyParent(ConfigCatalog.ConfigItem item) => false;
        private static float ReadNumber(ConfigCatalog.ConfigItem item, int component) => 0;
        private static void Rebuild() { }

        internal static Toggle BuildSwitch(GameObject template, Transform parent)
        {
            _toggleTemplate = template;
            BuildBoolRow(parent, new ConfigCatalog.ConfigItem(), null, null);
            return Rows[Rows.Count - 1].GetComponentInChildren<Toggle>();
        }
        internal static TMP_Dropdown BuildFallback(Transform parent)
        {
            BuildPresetRow(parent, new ConfigCatalog.ConfigItem { Section = "Sky", Key = "Style" }, null, null,
                new[] { "Default", "Cellar", "Swamp", "Black", "Mixed Reality" }, EnvironmentIndex(),
                index => ChooseEnvironment(index));
            return Rows[Rows.Count - 1].GetComponentInChildren<TMP_Dropdown>();
        }
        internal static bool Allowed => MixedRealityCanEnable;
        internal static string Hint => MixedRealityHint();
        internal static void Refresh() => RefreshMixedRealityAvailability();
        internal static void Clear() => ClearMixedRealityAvailabilityRefreshers();
        internal static void GenericEnable() => Apply(new ConfigCatalog.ConfigItem(), () => MixedReality.Enabled.Value = true);
        internal static void GenericDisable() => Apply(new ConfigCatalog.ConfigItem(), () => MixedReality.Enabled.Value = false);
        internal static bool ChooseMr() => ChooseMixedReality();
        internal static bool PickSky(SkyStyle style) => ChooseSky(style);
        internal static object MakeTile(Button button, Image picture, TMP_Text label)
        {
            var tile = new VariantTile { Available = () => MixedRealityCanEnable, Selected = () => MixedRealityOn,
                Choose = ChooseMixedReality, Hint = MixedRealityHint, Resource = "MR fixture" };
            var built = new List<BuiltTile> { new BuiltTile { Button = button, Picture = picture, Label = label, Tile = tile } };
            RegisterMixedRealityAvailabilityRefresh(() => RepaintVariantTiles(built));
            return new Action(() => OnVariantTileClicked(new ConfigCatalog.ConfigItem(), tile, built));
        }
    }
}
