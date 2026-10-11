using System;
using System.Collections.Generic;
using BepInEx.Configuration;

// The real BepInEx configuration library executes binding, list/range constraints and
// saved-file reloads. Unity row creation/TMP are explicit UI boundaries; these tests prove
// dropdown labels, index/value writes and conditional availability, not headset geometry.
namespace UnityEngine
{
    public sealed class Transform { }
    public sealed class GameObject { }
    public struct Vector2 { }
    public struct Vector3 { }
    public struct Vector4 { }
    public struct Color { }
    public static class Object { public static void Destroy(GameObject item) { } }
    public static class Mathf
    {
        public static int Clamp(int value, int min, int max) => Math.Clamp(value, min, max);
        public static float Clamp(float value, float min, float max) => Math.Clamp(value, min, max);
    }
}
namespace TMPro
{
    public sealed class TMP_Text { public string text = ""; }
    public sealed class TMP_Dropdown
    {
        public sealed class OptionData { public readonly string text; public OptionData(string value) { text = value; } }
        public sealed class Changed
        {
            private Action<int>? _callback;
            public void RemoveAllListeners() => _callback = null;
            public void AddListener(Action<int> action) => _callback += action;
            public void Invoke(int index) => _callback?.Invoke(index);
        }
        public readonly Changed onValueChanged = new();
        public readonly List<OptionData> options = new();
        public int value;
        public void ClearOptions() => options.Clear();
        public void AddOptions(List<OptionData> values) => options.AddRange(values);
        public void SetValueWithoutNotify(int index) => value = index;
        public void RefreshShownValue() { }
    }
}
namespace GloomhavenVR
{
    internal static class FrameLaunchOptIn
    {
        internal static bool Enabled;
        internal static bool MarkerExists(string path) => Enabled;
    }
}
namespace GloomhavenVR.Core
{
    internal static partial class PerfConfig
    {
        internal static void BindFixture(ConfigFile file) => BindFrameRendering(file);
    }
    internal static class VRLog { internal static void Warn(string area, string message) => throw new Exception(message); }
    internal static class Loc
    {
        internal static bool German;
        // Source-bound original Pair literals are generated alongside the fixture.
        internal static string Mod(string key) => Labels[key][German ? 1 : 0];
        internal static readonly Dictionary<string, string[]> Labels = OriginalWallLabels.All;
    }
}
namespace GloomhavenVR.WorldUI
{
    internal enum ConfigTopic { Visual }
    internal static partial class ConfigCatalog
    {
        internal static ConfigItem Item(ConfigEntryBase entry)
        {
            var result = new ConfigItem { Entry = entry, Section = entry.Definition.Section, Key = entry.Definition.Key };
            Classify(result);
            return result;
        }
        private static object[]? CuratedChoices(string section, string key) => null;
    }
    internal static partial class VROptionsTab
    {
        private static readonly UnityEngine.GameObject _toggleTemplate = new();
        private static readonly UnityEngine.GameObject _dropdownControl = new();
        private static readonly List<UnityEngine.GameObject> Rows = new();
        private static readonly Dictionary<string, ConfigCatalog.ConfigItem> ByKey = new();
        internal static TMPro.TMP_Dropdown LastDropdown = null!;
        internal static int Applied;
        private static string Id(string section, string key) => section + "/" + key;
        private static void EnsureLookup() { }
        private static UnityEngine.GameObject StampRow(UnityEngine.GameObject template, UnityEngine.Transform parent,
            out TMPro.TMP_Text? title, out UnityEngine.Transform? option)
        {
            title = new(); option = new();
            var row = new UnityEngine.GameObject(); Rows.Add(row); return row;
        }
        private static T? PlaceControl<T>(UnityEngine.Transform? option, UnityEngine.GameObject template) where T : new()
        {
            var control = new T(); LastDropdown = (TMPro.TMP_Dropdown)(object)control; return control;
        }
        private static string Caption(ConfigCatalog.ConfigItem item, string? caption) => caption ?? item.Key;
        private static void ApplyOptionCaption(TMPro.TMP_Text title) { }
        private static void IndentDependent(TMPro.TMP_Text title, ConfigCatalog.ConfigItem item) { }
        private static void ProbeCaptionFit(TMPro.TMP_Text title, string key) { }
        private static void AttachTooltip(UnityEngine.GameObject row, ConfigCatalog.ConfigItem item, TMPro.TMP_Text? title, string? hintKey) { }
        private static void Apply(ConfigCatalog.ConfigItem item, Action action) { action(); Applied++; }
        internal static void Build(ConfigCatalog.ConfigItem item) => BuildChoiceRow(new(), item, null, null);
        internal static void Add(ConfigCatalog.ConfigItem item) => ByKey[Id(item.Section, item.Key)] = item;
        internal static bool Available(ConfigCatalog.ConfigItem item) => DependencyMet(item);
        internal static bool NeedsRebuild(ConfigCatalog.ConfigItem item) => IsDependencyParent(item);
    }
}
