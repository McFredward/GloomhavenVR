using System;
using System.Collections.Generic;
using System.Linq;
using GloomhavenVR.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The curated tree, dependency filter and native-toggle binding are extracted from production.
// Unity controls and BepInEx persistence are explicit fixture boundaries, not pixel evidence.
namespace UnityEngine
{
    internal class Transform
    {
        internal object? Child;
        internal T? GetComponentInChildren<T>(bool includeInactive) where T : class => Child as T;
    }
    internal sealed class GameObject { }
    internal static class Object { internal static void Destroy(GameObject target) { } }
    internal static class Mathf { internal static int Max(int a, int b) => Math.Max(a, b); }
}
namespace UnityEngine.UI
{
    internal sealed class Toggle
    {
        internal sealed class Event
        {
            private readonly List<Action<bool>> _listeners = new();
            internal void RemoveAllListeners() => _listeners.Clear();
            internal void AddListener(Action<bool> action) => _listeners.Add(action);
            internal void Invoke(bool on) { foreach (Action<bool> listener in _listeners.ToArray()) listener(on); }
        }
        internal readonly Event onValueChanged = new();
        internal bool isOn;
        internal bool interactable = true;
        internal readonly UnityEngine.GameObject gameObject = new();
        internal void SetIsOnWithoutNotify(bool on) => isOn = on;
    }
}
namespace TMPro { internal sealed class TMP_Text { internal string text = ""; } }
namespace GloomhavenVR.Core
{
    internal static partial class Loc
    {
        internal static string CurrentLanguage = "English";
        internal static string? ConfigHelpForPlayers(string section, string key) => null;
        internal static (string En, string De) Pair(string en, string de) => (en, de);
        internal static string Mod(string key) => Texts.TryGetValue(key, out var pair)
            ? CurrentLanguage == "German" ? pair.De : pair.En : key;
    }
    internal static class VRLog { internal static void Warn(string module, string message) => throw new Exception(message); }
    internal static class VRSession { internal static bool IsRunning { get; set; } }
    // XR capability is an explicit port; native Frame availability is tested
    // by its own runtime suite, while this fixture exercises town option binding.
    internal enum FrameNativePassthroughStatus { Checking, Available }
    internal static class FrameNativePassthrough
    {
        internal static bool Required => FrameDefaults.Active;
        internal static bool IsAvailable => true;
        internal static FrameNativePassthroughStatus Status => FrameNativePassthroughStatus.Available;
    }
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
    internal static partial class FrameDefaults { internal static bool Active { get; set; } }
}
namespace GloomhavenVR.WorldUI.Surfaces
{
    internal static class CombatLogSurface { internal static void SpawnFromOptions() { } }
}
namespace GloomhavenVR.WorldUI
{
    internal class ConfigEntryBase { internal ConfigDescription? Description; }
    internal sealed class ConfigDescription
    {
        internal readonly string Description;
        internal ConfigDescription(string text, object? range = null) { Description = text; }
    }
    internal sealed class AcceptableValueRange<T> { internal AcceptableValueRange(T min,T max) { } }
    internal class Entry : ConfigEntryBase
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
        internal readonly Dictionary<string, Entry> Saved = new();
        internal Entry<T> Bind<T>(string section, string key, T value, string description) => Bind(section,key,value,new ConfigDescription(description));
        internal Entry<T> Bind<T>(string section, string key, T value, ConfigDescription description)
        {
            string id = section + "/" + key;
            if (Saved.TryGetValue(id, out Entry saved)) return (Entry<T>)saved;
            var result = new Entry<T>(value) { Description = description }; Saved[id] = result; return result;
        }
    }
    internal static partial class ConfigCatalog
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
        internal static readonly List<ConfigItem> Items = new();
        private static readonly ConfigGroup Group = new();
        internal const int TopicCount = 1;
        internal static IReadOnlyList<ConfigGroup> Groups(ConfigTopic topic)
        {
            Group.Items.Clear(); Group.Items.AddRange(Items);
            return new[] { Group };
        }
        internal static int TotalEntries => Items.Count;
        internal static void ToggleBool(ConfigItem item) => item.Entry.BoxedValue = !(item.Entry.BoxedValue is bool on && on);
    }
    internal static partial class VROptionsTab
    {
        private static int _curated;
        private static bool IsOpen => false;
        private static void AttachHoverHint(object target, string hint, string key) { }
        private static Transform ContentRoot = new();
        private static readonly Dictionary<string, ConfigCatalog.ConfigItem> ByKey = new(512);
        private static int _lookupSignature = -1;
        private static readonly List<ConfigCatalog.ConfigItem> _sectionItems = new();
        private static readonly List<CuratedEntry> _sectionEntries = new();
        private static readonly List<GameObject> Rows = new();
        private static readonly Dictionary<string, Toggle> Toggles = new();
        private static readonly Dictionary<string, string> Titles = new();
        private static readonly Dictionary<string, string?> Hints = new();
        private static readonly List<string> RenderOrder = new();
        private static readonly object _toggleTemplate = new();
        private static Toggle LastToggle = new();
        private static int GenericRows, DonorCalls, Saves, Assertions;
        private static string Id(string section, string key) => section + "/" + key;
        private static void Check(bool condition, string message)
        {
            Assertions++;
            if (!condition) throw new Exception("TOWN OPTIONS ASSERTION: " + message);
        }
        private static void BuildHeader(Transform parent, string label) => RenderOrder.Add("header:" + label);
        private static void BuildNote(Transform parent, string label, bool multiline = false) => RenderOrder.Add("note:" + label);
        private static string? VariantNote(List<ConfigCatalog.ConfigItem> items) => null;
        private static void BuildLinkRow(Transform parent, string label, Action action, bool asAction) => RenderOrder.Add("action:" + label);
        private static bool TryBuildVariantTiles(Transform parent, ConfigCatalog.ConfigItem item, string? caption, string? hint) => false;
        private static GameObject StampRow(object template, Transform parent, out TMP_Text? title, out Transform? option)
        {
            title = new(); LastToggle = new(); option = new() { Child = LastToggle };
            LastToggle.onValueChanged.AddListener(_ => DonorCalls++);
            return new();
        }
        private static string Caption(ConfigCatalog.ConfigItem item, string? caption) => caption ?? item.Display;
        private static void ApplyOptionCaption(TMP_Text title) { }
        private static void IndentDependent(TMP_Text title, ConfigCatalog.ConfigItem item) { }
        private static void ProbeCaptionFit(TMP_Text title, string key) { }
        private static TMP_Text ExistingValueLabel(GameObject row, TMP_Text? title) => new();
        private static void PaintToggleState(TMP_Text? state, bool on) { if (state != null) state.text = on ? "On" : "Off"; }
        private static void AttachTooltip(GameObject row, ConfigCatalog.ConfigItem item, TMP_Text? title, string? hint)
        {
            string key = Id(item.Section, item.Key);
            Toggles[key] = LastToggle; Titles[key] = title?.text ?? ""; Hints[key] = hint;
            RenderOrder.Add(key);
        }
        private static void Apply(ConfigCatalog.ConfigItem item, Action action) { action(); Saves++; }
        private static void Noop() { }
        private static void RebuildFixture()
        {
            RenderOrder.Clear(); Toggles.Clear(); Titles.Clear(); Hints.Clear(); GenericRows = 0;
            BuildCurated();
        }
        internal static void Run()
        {
            Cards.CardsConfig.CurrentBoard = Cards.ControlBoard.Oak;
            RetiredNpcConfig.Bind();
            Check(ConfigCatalog.IsRetired(RetiredNpcConfig.TownNpcDetailPercent),
                "actual advanced catalog retirement predicate hides the inert NPC key");
            RetiredNpcConfig.TownNpcDetailPercent.Value = 17; RetiredNpcConfig.Bind();
            Check(RetiredNpcConfig.TownNpcDetailPercent.Value == 17,
                "retiring the NPC slider preserves its existing persisted key value");
            FrameDefaults.Active = true;
            WorldUIConfig._file = new(); WorldUIConfig.Bind();
            Check(!WorldUIConfig.ImmersiveTownServices.Value, "fresh Frame uses original service windows");
            Check(WorldUIConfig.ImmersiveTownSpeech.Value && WorldUIConfig.ImmersiveTownSoundEffects.Value,
                "Frame master preference leaves saved child audio defaults independent");
            WorldUIConfig.ImmersiveTownServices.Value = true; WorldUIConfig.Bind();
            Check(WorldUIConfig.ImmersiveTownServices.Value, "Frame preserves explicit saved NPC opt-in");
            WorldUIConfig.ImmersiveTownServices.Value = false; FrameDefaults.Active = false; WorldUIConfig.Bind();
            Check(!WorldUIConfig.ImmersiveTownServices.Value, "profile detection never rewrites saved NPC opt-out");
            WorldUIConfig._file = new(); WorldUIConfig.Bind();
            Entry<bool> mode = WorldUIConfig.ImmersiveTownServices;
            Entry<bool> speech = WorldUIConfig.ImmersiveTownSpeech;
            Entry<bool> effects = WorldUIConfig.ImmersiveTownSoundEffects;
            Check(mode.Value && speech.Value && effects.Value, "fresh installation enables NPCs and both local audio settings");
            _curated = Array.FindIndex(Curated, c => c.LocKey == "cat_environment");
            Check(_curated >= 0, "environment category exists");
            CuratedSection map = Curated[_curated].Sections.Single(s => s.LocKey == "sec_map3d");
            string[] keys = map.Entries.Where(e => !e.IsAction).Select(e => e.Key).ToArray();
            int mapSwitch = Array.IndexOf(keys, "Vanilla2DMap");
            Check(mapSwitch >= 0 && keys.Skip(mapSwitch + 1).Take(3).SequenceEqual(new[]
                { "ImmersiveTownServices", "ImmersiveTownSpeech", "ImmersiveTownSoundEffects" }),
                "three town switches follow the 2D-map switch in Environment");
            var declared = Curated.SelectMany(c => c.Sections).SelectMany(s => s.Entries).Where(e => !e.IsAction).ToArray();
            Check(!declared.Any(e => e.Section == "Optimize" && e.Key == "TownNpcDetailPercent"),
                "removed NPC detail slider has no curated home");
            foreach (string key in new[] { "ImmersiveTownServices", "ImmersiveTownSpeech", "ImmersiveTownSoundEffects" })
                Check(declared.Count(e => e.Section == "WorldUI" && e.Key == key) == 1, "town setting has one curated home: " + key);
            foreach (CuratedEntry declaredEntry in declared)
            {
                var item = new ConfigCatalog.ConfigItem { Section = declaredEntry.Section, Key = declaredEntry.Key };
                if (item.Section == "WorldUI" && item.Key == "ImmersiveTownServices") item.Entry = mode;
                if (item.Section == "WorldUI" && item.Key == "ImmersiveTownSpeech") item.Entry = speech;
                if (item.Section == "WorldUI" && item.Key == "ImmersiveTownSoundEffects") item.Entry = effects;
                if (item.Section == "Rig" && item.Key == "Vanilla2DMap") item.Entry = new Entry<bool>(false);
                if (!ConfigCatalog.Items.Any(existing => existing.Section == item.Section && existing.Key == item.Key)) ConfigCatalog.Items.Add(item);
            }
            var target = Lookup("WorldUI", "ImmersiveTownServices")!;
            var flat = Lookup("Rig", "Vanilla2DMap")!;
            Check(ReferenceEquals(target.Entry, mode), "curated row binds to persisted bool");
            Check(!HasSpecialRow(target), "mode uses ordinary native toggle, not dropdown");
            foreach (string language in new[] { "English", "German" })
            foreach (bool enabled in new[] { true, false })
            {
                Loc.CurrentLanguage = language; flat.Entry.BoxedValue = false; mode.Value = enabled;
                int before = mode.Writes;
                RebuildFixture();
                string id = "WorldUI/ImmersiveTownServices";
                Check(Toggles.ContainsKey(id), "NPC toggle visible in 3D map even when turned off");
                Check(Toggles[id].isOn == enabled, "toggle matches persisted bool");
                Check(Titles[id] == (language == "German" ? "Immersive Stadt-NPCs" : "Immersive town NPCs"), "caption localized");
                Check(Hints[id] == "h_vr_o_immersivetown" && Loc.Mod(Hints[id]!) != Hints[id], "hint localized");
                Check(mode.Writes == before, "opening options does not write setting");
                Check(Toggles.ContainsKey("WorldUI/ImmersiveTownSpeech") == enabled
                    && Toggles.ContainsKey("WorldUI/ImmersiveTownSoundEffects") == enabled,
                    "child audio settings fold under master switch");
            }
            flat.Entry.BoxedValue = true; RebuildFixture();
            Check(!Toggles.Keys.Any(k => k.StartsWith("WorldUI/ImmersiveTown", StringComparison.Ordinal)),
                "2D map hides entire immersive NPC setting family");
            flat.Entry.BoxedValue = false; mode.Value = true;
            int changes = 0; mode.Changed += () => changes++;
            for (int i = 0; i < 32; i++)
            {
                RebuildFixture(); Toggles["WorldUI/ImmersiveTownServices"].onValueChanged.Invoke(false);
                Check(!mode.Value, "first press disables NPCs immediately");
                RebuildFixture();
                Check(!Toggles["WorldUI/ImmersiveTownServices"].isOn, "reopened toggle remembers off");
                Toggles["WorldUI/ImmersiveTownServices"].onValueChanged.Invoke(true);
                Check(mode.Value, "second press enables NPCs immediately");
                Check(DonorCalls == 0, "native donor callbacks stripped from clone");
            }
            Check(changes == 64 && Saves == 64, "one notification and save per deliberate press");
            Check(GraphicsProfileActions.Calls == 0, "constructing and reopening town controls never executes stored graphics actions");
            Console.WriteLine($"Town options: {Assertions} assertions passed.");
        }
    }
}
namespace GloomhavenVR.WorldUI
{
    // Unrelated action boundary. The complete production graphics action has its own suite;
    // this fixture compiles the real curated table and checks that construction is deferred.
    internal static class GraphicsProfileActions
    {
        internal static int Calls;
        internal static void Apply(int index) { Calls++; }
    }
}
internal static class Program { private static void Main() => GloomhavenVR.WorldUI.VROptionsTab.Run(); }
