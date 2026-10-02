using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

// The actual Bind/entry-point/cancel methods are extracted unchanged from production.
// These doubles represent configuration persistence and Unity construction boundaries only.
namespace BepInEx
{
    internal static class Paths { internal const string BepInExRootPath = "test-install"; }
}

namespace BepInEx.Configuration
{
    internal sealed class ConfigEntry<T>
    {
        private T _value;
        internal ConfigEntry(T value) => _value = value;
        internal event EventHandler? SettingChanged;
        internal T Value
        {
            get => _value;
            set { if (EqualityComparer<T>.Default.Equals(_value, value)) return;
                _value = value; SettingChanged?.Invoke(this, EventArgs.Empty); }
        }
    }
    internal sealed class ConfigFile
    {
        internal bool? SavedEnabled;
        internal string EnabledKey = "";
        internal bool BoundDefault;
        internal ConfigEntry<bool>? Entry;
        internal ConfigEntry<T> Bind<T>(string section, string key, T value, object description)
        {
            if (key == "WindowMaterialise")
            {
                EnabledKey = section + "/" + key;
                BoundDefault = (bool)(object)value!;
                Entry = new ConfigEntry<bool>(SavedEnabled ?? BoundDefault);
                return (ConfigEntry<T>)(object)Entry;
            }
            return new ConfigEntry<T>(value);
        }
    }
    internal sealed class ConfigDescription { internal ConfigDescription(string text, object range) { } }
    internal sealed class AcceptableValueRange<T> { internal AcceptableValueRange(T min, T max) { } }
}

namespace GloomhavenVR
{
    internal static class FrameLaunchOptIn
    {
        internal static bool Marked;
        internal static bool MarkerExists(string root) => Marked;
    }
}

namespace GloomhavenVR.WorldUI
{
    internal static class WorldUIConfig { internal static ConfigFile? FileHandle; }
    internal sealed class WindowMaterialisePreRoll
    {
        internal bool Ended;
        internal void End(string reason) { Ended = true; WindowMaterialise.RemovePreRoll(this); }
    }
    internal static class ModalFallback
    {
        internal static bool HasNothingToDissolve(ConvertedPanel panel, out string reason)
        { reason = ""; return false; }
    }
    internal static partial class WindowMaterialise
    {
        private const string Scope = "WorldUI";
        private const float HardCeilingSeconds = 2f, MinSeconds = .05f;
        private static ConfigEntry<bool>? _enabled;
        private static ConfigEntry<float>? _appearSeconds, _vanishSeconds, _intensity;
        private static bool _bound;
        private static readonly List<WindowMaterialiseRunner> Live = new();
        private static readonly List<WindowMaterialisePreRoll> PreRolls = new();
        private static int _outDeadPanel, _outStray, _outEffectOff, _outHoverCard,
            _outSkipped, _outZeroLength, _outStarted;
        internal static int ReleasedMeshes;
        private static float Clamp(float value) => value < MinSeconds ? 0 : MathF.Min(value, HardCeilingSeconds);
        private static void EnsureSubscribed() { }
        private static bool IsHoverCard(ConvertedPanel panel) => false;
        private static string Name(ConvertedPanel panel) => "test panel";
        private static string SpawnClause(ConvertedPanel panel) => "";
        private static void DropPreRoll(ConvertedPanel panel, string reason) { }
        private static void DetachInput(ConvertedPanel panel) => panel.InputDetached = true;
        private static void NoteStraySuppressed(ConvertedPanel panel, string reason) { }
        private static void NotePlayOutOutcome(ConvertedPanel? panel, ref int count,
            string reason, string detail = "") { count++; }
        private static bool DrawnOnPreviousFrame(ConvertedPanel panel, out string reason)
        { reason = ""; return true; }
        private static WindowMaterialiseRunner? FindVanishing(ConvertedPanel panel)
        { foreach (var runner in Live) if (ReferenceEquals(runner.Panel, panel) && runner.Vanishing) return runner; return null; }
        internal static void ResetSwitch(bool frame, bool? saved = null)
        {
            CancelAll("test reset"); _bound = false; _enabled = null;
            FrameLaunchOptIn.Marked = frame;
            WorldUIConfig.FileHandle = new ConfigFile { SavedEnabled = saved };
            ReleasedMeshes = 0; WindowMaterialiseRunner.BeginCalls = 0;
        }
        internal static int LiveCount => Live.Count;
        internal static WindowMaterialiseRunner Last => Live[^1];
        internal static WindowMaterialisePreRoll AddPreRoll()
        { var hold = new WindowMaterialisePreRoll(); PreRolls.Add(hold); return hold; }
        internal static void RemovePreRoll(WindowMaterialisePreRoll hold) => PreRolls.Remove(hold);
    }
    internal sealed partial class WindowMaterialiseRunner
    {
        internal static int BeginCalls;
        internal VisibilityHold CurrentHold => _hold!;
        internal static bool Begin(ConvertedPanel panel, float seconds, bool materialising, Action? onDone)
        {
            BeginCalls++;
            var runner = new WindowMaterialiseRunner(panel, materialising, onDone ?? (() => { }));
            // Runner allocation itself belongs to the actual-Unity/debris harness; these
            // branches assert the production master gate reaches no construction while OFF.
            runner._debris = new() { MeshFront = new GameObject(), MeshBehind = new GameObject() };
            WindowMaterialise.Register(runner);
            runner.BeginBacking(); runner.Frame(0);
            return true;
        }
        internal void ChainOnDone(Action done) => _onDone += done;
    }
}
