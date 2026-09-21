using System;
using System.Collections.Generic;
using GloomhavenVR.Net.TownServices;
using UnityEngine;

// The production publisher Tick is bound verbatim. Native game catalogues and the
// publication sink below are fixtures: these tests establish routing, provenance
// arguments and removal, while Program.cs validates actual capture/render playback.
namespace GloomhavenVR.WorldUI
{
    internal sealed class PublisherWindow : MonoBehaviour { }
    internal sealed class TownServiceSurface
    {
        internal ushort Id;
        internal PanelFixture Panel = new();
    }
    internal sealed class PanelFixture { internal Transform Target = null!; }
    internal sealed class TownServiceCatalog
    {
        internal sealed class Control { internal string Key = ""; internal TownServiceSurface Surface = new(); }
        internal sealed class Entry
        {
            internal bool Current = true;
            internal int ItemId;
            internal Transform CardRoot = null!;
            internal Transform? RowContent;
            internal Transform RowSource = null!;
            internal Transform? RowCloneOf(Transform source) => source == RowSource ? RowContent : null;
        }
        internal List<Control> Controls = new();
        internal Transform NavigationRoot = null!;
        internal List<Entry> Entries = new();
    }
    internal sealed class TownServiceToken
    {
        internal Transform? HeldContent;
        internal Transform Source = null!;
    }
    internal sealed class TownServiceTray { internal Transform Root = null!; }
    internal sealed class UITooltip : MonoBehaviour { internal Transform? m_AnchorToTarget; }
    internal static class NativeTemplates
    {
        internal static bool Ready = true;
        internal static UITooltip? Tooltip;
        internal static Transform? Original(string key) => null;
        internal static string TooltipKey(UITooltip tooltip) => "tooltip.fixture";
    }
    internal static class TownServicePresentation
    {
        internal static bool Active = true;
        internal static byte Service = 1;
        internal static uint Session = 1200;
        internal static float SessionAge;
        internal static PublisherWindow? Window;
        internal static TownServiceCatalog? Catalog;
        internal static List<TownServiceSurface> LocalSurfaces = new();
        internal static List<TownServiceToken> Samples = new();
        internal static TownServiceTray? Tray;
    }
    internal static partial class TownServiceSync
    {
        internal sealed class Recorded
        { internal string Key = ""; internal Transform Source = null!; internal Transform? Provenance; internal Func<Transform, Transform?>? CloneOf; }
        private sealed class Published { internal ushort Id; internal bool Seen; }
        private sealed class SourceEntry { internal bool Seen; }
        private static readonly Dictionary<string, Published> Modules = new();
        private static readonly Dictionary<Transform, SourceEntry> Sources = new();
        private static readonly HashSet<Transform> Visited = new();
        private static readonly List<Transform> Dynamic = new(), RemovedSources = new();
        private static readonly List<string> Removed = new();
        private static uint _session;
        private static byte _service;
        private static ushort _nextId;
        private static Transform? _sharedFrame;
        internal static readonly List<Recorded> Calls = new();
        internal static int ModuleCount => Modules.Count;
        internal static int SourceCount => Sources.Count;
        private static void Prepare() { }
        private static Transform? ResolveFrame(int peer) => _sharedFrame;
        private static void Reset() { Modules.Clear(); Sources.Clear(); _session = 0; _service = 0; }
        private static string? DynamicKey(Transform source) => null;
        private static void PublishHeld(TownServiceToken sample, Transform original) { }
        private static bool OwnsAnchor(Transform? anchor) => false;
        private static void Publish(string key, Transform? source, Transform? provenance = null, Func<Transform, Transform?>? cloneOf = null)
        {
            if (source == null) return;
            Calls.Add(new() { Key = key, Source = source, Provenance = provenance, CloneOf = cloneOf });
            Sources[source] = new() { Seen = true };
            string identity = key + "@" + source.GetInstanceID();
            if (!Modules.TryGetValue(identity, out Published? module)) Modules.Add(identity, module = new() { Id = ++_nextId });
            module.Seen = true;
        }
    }
}
