using System;
using System.Collections.Generic;
using System.Linq;
using GloomhavenVR.Core;
using TMPro;
using UnityEngine;

// Native Unity/TMP controls and BepInEx persistence are explicit fixture boundaries.
// The curated tree, dependency/variant gates, row dispatch and dropdown construction/callback
// are compiled from production by the runner. This verifies menu structure and behavior, not
// headset pixels, native template availability, or the town station's separate live rollback.
namespace UnityEngine
{
    internal class Transform { }
    internal sealed class GameObject { }
    internal static class Mathf
    {
        internal static int Clamp(int n, int min, int max) => Math.Clamp(n, min, max);
        internal static int Max(int a, int b) => Math.Max(a, b);
    }
}
namespace TMPro
{
    internal sealed class TMP_Text { internal string text = ""; }
    internal sealed class TMP_Dropdown
    {
        internal sealed class OptionData { internal string text; internal OptionData(string value) => text = value; }
        internal sealed class Event
        {
            private readonly List<Action<int>> _listeners = new();
            internal void RemoveAllListeners() => _listeners.Clear();
            internal void AddListener(Action<int> action) => _listeners.Add(action);
            internal void Invoke(int index) { foreach (Action<int> listener in _listeners.ToArray()) listener(index); }
        }
        internal readonly Event onValueChanged = new();
        internal readonly List<OptionData> options = new();
        internal int value;
        internal void ClearOptions() => options.Clear();
        internal void AddOptions(List<OptionData> values) => options.AddRange(values);
        internal void SetValueWithoutNotify(int index) => value = index;
        internal void RefreshShownValue() { }
    }
}
namespace GloomhavenVR.Core
{
    internal static partial class Loc
    {
        internal static string CurrentLanguage = "English";
        internal static (string En, string De) Pair(string en, string de) => (en, de);
        internal static string Mod(string key) => Texts.TryGetValue(key, out var pair)
            ? CurrentLanguage == "German" ? pair.De : pair.En : key;
    }
    internal static class VRLog { internal static void Warn(string module, string message) => throw new Exception(message); }
}
namespace GloomhavenVR.Cards
{
    internal enum ControlBoard { Oak, Steel, Bronze }
    internal static class CardsConfig { internal static ControlBoard CurrentBoard; }
    internal static class CardsDriver { internal static void RequestBoardRecall() { } }
}
namespace GloomhavenVR.Hands
{
    internal enum HandStyle { Glove, Plate, Arcane }
    internal static class HandStyles { internal static HandStyle Clamp(int n) => (HandStyle)Math.Clamp(n, 0, 2); }
}
namespace GloomhavenVR
{
    internal static class Plugin { internal static WorldUI.Entry<Hands.HandStyle> HandStyle = new(Hands.HandStyle.Glove); }
}
namespace GloomhavenVR.WorldUI.Surfaces
{
    internal static class CombatLogSurface { internal static void SpawnFromOptions() { } }
}
namespace GloomhavenVR.WorldUI
{
    internal class Entry
    {
        private object _value = true;
        internal int Writes;
        internal event Action? Changed;
        internal object BoxedValue { get => _value; set { _value = value; Writes++; Changed?.Invoke(); } }
    }
    internal sealed class Entry<T> : Entry
    {
        internal Entry(T value) { BoxedValue = value!; Writes = 0; }
        internal T Value { get => (T)BoxedValue; set => BoxedValue = value!; }
    }
    internal sealed class ConfigFile
    {
        internal Entry<T> Bind<T>(string section, string key, T value, string description) => new(value);
    }
    internal static class ConfigCatalog
    {
        internal enum ConfigKind { Bool, Number }
        internal enum ConfigTopic { Panels }
        internal sealed class ConfigItem
        {
            internal string Section = "", Key = "", Display = "";
            internal Entry Entry = new();
            internal ConfigKind Kind = ConfigKind.Bool;
            internal int Components = 1;
        }
        internal sealed class ConfigGroup { internal readonly List<ConfigItem> Items = new(); }
        internal const int TopicCount = 1;
        internal static readonly ConfigGroup Group = new();
        internal static int TotalEntries => Group.Items.Count;
        internal static IReadOnlyList<ConfigGroup> Groups(ConfigTopic _) => new[] { Group };
    }
    internal static partial class VROptionsTab
    {
        private static int _curated;
        private static Transform ContentRoot = new();
        private static readonly Dictionary<string, ConfigCatalog.ConfigItem> ByKey = new(512);
        private static int _lookupSignature = -1;
        private static readonly List<ConfigCatalog.ConfigItem> _sectionItems = new();
        private static readonly List<CuratedEntry> _sectionEntries = new();
        private static string Id(string section, string key) => section + "/" + key;
        private static readonly object _toggleTemplate = new(), _dropdownControl = new();
        private static readonly List<string> RenderOrder = new();
        private static TMP_Text Title = new();
        private static TMP_Dropdown Dropdown = new();
        private static string? Hint;
        private static int GenericRows, DonorCalls, Saves, Assertions;
        private static void Check(bool condition, string message)
        {
            Assertions++;
            if (!condition) throw new Exception("TOWN OPTIONS ASSERTION: " + message);
        }
        private static void BuildHeader(Transform _, string label) => RenderOrder.Add("header:" + label);
        private static void BuildNote(Transform _, string label) => RenderOrder.Add("note:" + label);
        private static string? VariantNote(List<ConfigCatalog.ConfigItem> _) => null;
        private static void BuildLinkRow(Transform _, string label, Action action, bool asAction) => RenderOrder.Add("action:" + label);
        private static bool TryBuildVariantTiles(Transform _, ConfigCatalog.ConfigItem item, string? caption, string? hint) => false;
        private static GameObject StampRow(object template, Transform parent, out TMP_Text? title, out Transform? option)
        {
            title = Title = new(); option = new Transform(); Dropdown = new();
            Dropdown.onValueChanged.AddListener(_ => DonorCalls++);
            RenderOrder.Add("dropdown"); return new();
        }
        private static T? PlaceControl<T>(Transform? option, object template) where T : class => Dropdown as T;
        private static string Caption(ConfigCatalog.ConfigItem item, string? caption) => caption ?? item.Display;
        private static void ApplyOptionCaption(TMP_Text _) { }
        private static void IndentDependent(TMP_Text _, ConfigCatalog.ConfigItem item) { }
        private static void ProbeCaptionFit(TMP_Text _, string key) { }
        private static void AttachTooltip(GameObject _, ConfigCatalog.ConfigItem item, TMP_Text? title, string? hint) => Hint = hint;
        private static void Apply(ConfigCatalog.ConfigItem item, Action action) { action(); Saves++; }
        private static void RebuildFixture()
        {
            RenderOrder.Clear(); GenericRows = 0; Hint = null; Title = new(); Dropdown = new();
            BuildCurated();
        }
        internal static void Run()
        {
            WorldUIConfig.Bind();
            Entry<bool> entry = WorldUIConfig.ImmersiveTownServices;
            Entry<bool> speech = WorldUIConfig.ImmersiveTownSpeech;
            Entry<bool> effects = WorldUIConfig.ImmersiveTownSoundEffects;
            Check(entry.Value, "fresh installation defaults to immersive NPCs");
            Check(speech.Value && effects.Value, "fresh installation enables resident speech and physical effects");
            _curated = Array.FindIndex(Curated, c => c.LocKey == "cat_panels");
            Check(_curated >= 0, "panels category exists");
            CuratedCategory panels = Curated[_curated];
            Check(panels.Sections[0].LocKey == "vr_sec_townservices", "town services are the first panel section above other readouts");
            Check(panels.Sections[0].Entries.Length == 3
                && panels.Sections[0].Entries[0].Key == "ImmersiveTownServices"
                && panels.Sections[0].Entries[1].Key == "ImmersiveTownSpeech"
                && panels.Sections[0].Entries[2].Key == "ImmersiveTownSoundEffects",
                "town mode and both local audio controls occupy the first section");
            var declared = Curated.SelectMany(c => c.Sections).SelectMany(s => s.Entries).Where(e => !e.IsAction).ToArray();
            Check(declared.Count(e => e.Section == "WorldUI" && e.Key == "ImmersiveTownServices") == 1, "town mode has exactly one curated home");
            Check(declared.Count(e => e.Section == "WorldUI" && e.Key == "ImmersiveTownSpeech") == 1
                && declared.Count(e => e.Section == "WorldUI" && e.Key == "ImmersiveTownSoundEffects") == 1,
                "each town audio preference has exactly one curated home");
            foreach (CuratedEntry declaredEntry in declared)
            {
                var item = new ConfigCatalog.ConfigItem { Section = declaredEntry.Section, Key = declaredEntry.Key };
                if (item.Section == "WorldUI" && item.Key == "ImmersiveTownServices") item.Entry = entry;
                if (item.Section == "WorldUI" && item.Key == "ImmersiveTownSpeech") item.Entry = speech;
                if (item.Section == "WorldUI" && item.Key == "ImmersiveTownSoundEffects") item.Entry = effects;
                if (!ConfigCatalog.Group.Items.Any(existing => existing.Section == item.Section && existing.Key == item.Key)) ConfigCatalog.Group.Items.Add(item);
            }
            ConfigCatalog.ConfigItem target = Lookup("WorldUI", "ImmersiveTownServices")!;
            Check(ReferenceEquals(target.Entry, entry), "lookup resolves the existing persisted bool");
            Check(ReferenceEquals(Lookup("WorldUI", "ImmersiveTownSpeech")!.Entry, speech)
                && ReferenceEquals(Lookup("WorldUI", "ImmersiveTownSoundEffects")!.Entry, effects),
                "audio rows resolve their independent persisted booleans");
            foreach (string language in new[] { "English", "German" })
            foreach (bool enabled in new[] { true, false })
            foreach (Cards.ControlBoard board in Enum.GetValues<Cards.ControlBoard>())
            foreach (Hands.HandStyle hand in Enum.GetValues<Hands.HandStyle>())
            {
                Loc.CurrentLanguage = language; entry.Value = enabled;
                Cards.CardsConfig.CurrentBoard = board; Plugin.HandStyle.Value = hand;
                int before = entry.Writes;
                RebuildFixture();
                Check(RenderOrder[0] == "header:" + (language == "German" ? "Händler, Tempel & Verzauberin" : "Merchant, temple & enchantress"), "visible heading names all three services");
                Check(RenderOrder[1] == "dropdown", "explicit mode control is rendered before other settings");
                Check(Dropdown.options.Count == 2, "mode control offers both presentations");
                Check(Dropdown.options[0].text == "Immersive NPCs", "NPC choice is visible");
                Check(Dropdown.options[1].text == (language == "German" ? "Originale Fenster" : "Original windows"), "window choice is localized");
                Check(Dropdown.value == (enabled ? 0 : 1), "displayed selection agrees with persisted bool");
                Check(Title.text == (language == "German" ? "Stadtbesuch-Modus" : "Town service mode"), "mode caption is localized");
                Check(Hint == "h_vr_o_immersivetown" && Loc.Mod(Hint) != Hint, "mode row retains localized explanation");
                Check(entry.Writes == before, "opening settings does not change persisted presentation");
            }
            int changes = 0; entry.Changed += () => changes++;
            for (int repeat = 0; repeat < 32; repeat++)
            {
                RebuildFixture(); Dropdown.onValueChanged.Invoke(1);
                Check(!entry.Value, "window selection immediately updates existing live setting");
                RebuildFixture();
                Check(Dropdown.value == 1, "reopening after disable remembers window mode");
                Dropdown.onValueChanged.Invoke(0);
                Check(entry.Value, "NPC selection immediately updates existing live setting");
                Check(DonorCalls == 0, "native donor callbacks cannot survive control cloning");
            }
            Check(changes == 64 && Saves == 64, "one live-setting notification and save per deliberate selection");
            int writes = entry.Writes;
            Dropdown.onValueChanged.Invoke(-1); Dropdown.onValueChanged.Invoke(2);
            Check(entry.Writes == writes, "invalid dropdown indices cannot alter presentation");
            foreach (ConfigCatalog.ConfigItem item in ConfigCatalog.Group.Items)
                if (!ReferenceEquals(item, target)) item.Entry.BoxedValue = false;
            entry.Value = false; RebuildFixture();
            Check(RenderOrder[1] == "dropdown" && Dropdown.value == 1, "disabled neighboring features cannot hide mode choice");
            target.Display = Loc.Mod("vr_o_immersivetown");
            BuildItem(target);
            Check(Title.text == "Stadtbesuch-Modus" && Dropdown.options.Count == 2, "advanced item dispatch retains named presentation choices");
            Check(!TryBuildTownServiceModeRow(ContentRoot, new ConfigCatalog.ConfigItem { Section = "WorldUI", Key = "WristHud" }, null, null), "unrelated booleans retain their normal control");
            Console.WriteLine($"Town options: {Assertions} assertions passed.");
        }
    }
}
internal static class Program { private static void Main() => GloomhavenVR.WorldUI.VROptionsTab.Run(); }
