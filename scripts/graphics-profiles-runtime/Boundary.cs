using System;
using System.Collections.Generic;
using System.Reflection;

namespace BepInEx
{
    internal static class Paths { internal static string BepInExRootPath => "fixture"; }
}
namespace BepInEx.Configuration
{
    public readonly record struct ConfigDefinition(string Section, string Key);
    public sealed class ConfigFile
    {
        internal readonly Dictionary<ConfigDefinition, object> Entries = new();
        public bool SaveOnConfigSet { get; set; } = true;
        public int Saves;
        public bool ThrowOnSave;
        public bool TryGetEntry<T>(ConfigDefinition definition, out ConfigEntry<T> entry)
        { entry = Entries.TryGetValue(definition, out object? value) ? value as ConfigEntry<T> ?? null! : null!; return entry != null; }
        public void Save() { Saves++; if (ThrowOnSave) throw new System.IO.IOException(); }
        internal ConfigEntry<T> Add<T>(string section, string key, T value)
        { var entry = new ConfigEntry<T>(this, value); Entries.Add(new(section, key), entry); return entry; }
    }
    public sealed class ConfigEntry<T>
    {
        private T _value;
        private readonly ConfigFile _file;
        public int Writes;
        public bool ThrowOnWrite;
        internal ConfigEntry(ConfigFile file, T value) { _file = file; _value = value; }
        public T Value { get => _value; set { if (ThrowOnWrite) throw new InvalidOperationException(); _value = value; Writes++; if (_file.SaveOnConfigSet) _file.Save(); } }
    }
}
namespace UnityEngine
{
    public class Object
    {
        internal static Gloomhaven.GraphicSettings[] Settings = Array.Empty<Gloomhaven.GraphicSettings>();
        public static T[] FindObjectsOfType<T>(bool inactive) where T : Object => (T[])(object)Settings;
    }
    public sealed class GameObject { public Scene scene = new(); }
    public sealed class Scene { internal bool Valid = true; public bool IsValid() => Valid; }
    public static class QualitySettings
    { public static string[] names = { "Fastest", "Fast", "Simple", "Good", "Beautiful", "Fantastic" }; public static string Selected = "Custom"; }
}
namespace Gloomhaven
{
    public sealed class GraphicSettings : UnityEngine.Object
    {
        public readonly UnityEngine.GameObject gameObject = new();
        // Boundary for native graphics initialization/callback only. The production adapter
        // is compiled unchanged; game continuation and rendering are outside this fixture.
        private readonly object? levelOpts;
        private readonly object? unityProfiles;
        public int Calls, Saves;
        public bool ThrowOnCallback;
        public GraphicSettings(bool ready = true)
        { levelOpts = ready ? new object() : null; unityProfiles = ready ? new object() : null; }
        private void SetQualityLevel(string level)
        { if (levelOpts == null || unityProfiles == null || ThrowOnCallback) throw new InvalidOperationException(); Calls++; Saves++; UnityEngine.QualitySettings.Selected = level; }
    }
}
public sealed class SettingsHolder
{
    private readonly Gloomhaven.GraphicSettings _graphicSettings;
    public SettingsHolder(Gloomhaven.GraphicSettings settings) { _graphicSettings = settings; }
}
public sealed class SceneController
{
    public static SceneController? Instance;
    public SettingsHolder SettingsHolder;
    public SceneController(Gloomhaven.GraphicSettings settings) { SettingsHolder = new(settings); }
}
namespace HarmonyLib
{
    public static class AccessTools
    {
        public static FieldInfo? Field(Type type, string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        public static MethodInfo? Method(Type type, string name, Type[] types) => type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic, null, types, null);
    }
}
namespace GloomhavenVR.Core
{
    // Only the host-platform signal is a fixture boundary. The production
    // shared default selector and full profile action are compiled unchanged.
    internal static class QuestStandalonePlatform { internal static bool Enabled; }
    internal static class ModuleConfig
    {
        internal static readonly Dictionary<string, BepInEx.Configuration.ConfigFile> Files = new();
        internal static BepInEx.Configuration.ConfigFile Get(string name)
        { if (!Files.TryGetValue(name, out var file)) Files.Add(name, file = new()); return file; }
        internal static KeyValuePair<string, BepInEx.Configuration.ConfigFile>[] Snapshot() => new List<KeyValuePair<string, BepInEx.Configuration.ConfigFile>>(Files).ToArray();
    }
    internal static class VRLog
    {
        internal static int Warnings;
        internal static void Info(string topic, string text) { }
        internal static void Warn(string topic, string text) { Warnings++; }
    }
}
namespace GloomhavenVR
{
    internal static class FrameLaunchOptIn
    {
        internal static bool MarkerPresent;
        internal static bool MarkerExists(string path) => MarkerPresent;
    }
}
