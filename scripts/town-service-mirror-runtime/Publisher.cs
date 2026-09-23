using System;
using System.Collections.Generic;
using GloomhavenVR.Net.TownServices;
using UnityEngine;

// The production publisher Tick is bound verbatim. Native game catalogues and the
// publication sink below are fixtures: these tests establish routing, provenance
// arguments and removal, while Program.cs validates actual capture/render playback.
namespace GloomhavenVR.WorldUI
{
    internal sealed class UIShopItemSlot : MonoBehaviour { }
    internal sealed class UITempleShopSlot : MonoBehaviour { }
    internal sealed class UINewEnhancementShopSlot : MonoBehaviour { }
    internal sealed class UIEnhanceCardSlot : MonoBehaviour { }
    internal sealed class UIEnhanceCardPoint : MonoBehaviour { }
    internal sealed class UIEnhancementButtonHighlight : MonoBehaviour { }
    internal sealed class ItemCardUI : MonoBehaviour { internal int CardID; }
    internal sealed class AbilityCardUI : MonoBehaviour { internal int CardID; internal Transform fullAbilityCard = null!; }
    internal static class TownServiceAssets { internal static GameObject? Furniture; internal static GameObject? Prefab(string name) => Furniture; }
    internal sealed class TownServiceWorkspace { internal sealed class Prop { internal string Key = ""; internal Transform Root = null!; } }
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
            internal bool Current = true, Exposed = true;
            internal TownServiceToken Sample = new();
            internal int ItemId;
            internal Transform CardRoot = null!;
            internal Transform? BodyRoot;
            internal Transform? RowContent;
            internal Transform RowSource = null!;
            internal Transform? RowCloneOf(Transform source) => source == RowSource ? RowContent : null;
        }
        internal Transform? PreviewContent, PreviewSource, HintContent;
        internal Transform? HintSource;
        internal readonly Dictionary<Transform, Transform> PreviewMap = new(), HintMap = new();
        internal Transform? PreviewCloneOf(Transform source) => PreviewMap.TryGetValue(source, out var copy) ? copy : null;
        internal Transform? HintCloneOf(Transform source) => HintMap.TryGetValue(source, out var copy) ? copy : null;
        internal List<Control> Controls = new();
        internal Transform NavigationRoot = null!;
        internal List<Entry> Entries = new();
        internal List<TownServiceMerchantDrawer> Drawers = new();
        internal List<TownServiceMerchantCounter> Extensions = new();
        internal List<TownServiceMerchantZone> Zones = new();
    }
    internal sealed partial class TownServiceMerchantDrawer { internal Transform Root = null!, HousingRoot = null!; internal bool Moving; }
    internal sealed class TownServiceMerchantCounter { internal Transform Root = null!; }
    internal sealed class TownServiceEnhancementHandoff {
        internal sealed class ReturnPresentation { internal Transform? Face, Body, StationRoot; internal int CardId; internal uint Session; internal float SessionAge; }
        internal static readonly List<ReturnPresentation> Returning = new();
        internal Transform? Card, Face, Zone; internal AbilityCardUI? NativeSource;
        internal Transform? CloneOf(Transform original) => NativeSource != null && original == NativeSource.fullAbilityCard ? Face : null;
    }
    internal sealed class TownServiceMerchantZone { internal Transform Root = null!; }
    internal sealed class TownServiceRitual
    {
        internal TownServiceEnhancementHandoff? Handoff;
        internal sealed class Piece
        {
            internal TownServiceToken Token = new();
            internal string Key = "", BodyKey = "merchant.cardbody", DetailKey = "";
            internal Transform Source = null!, Content = null!, Body = null!;
            internal Transform? DetailSource, DetailContent;
            internal Transform? CloneOf(Transform source) => source == Source ? Content : null;
            internal Transform? DetailCloneOf(Transform source) => source == DetailSource ? DetailContent : null;
        }
        internal sealed class Inscription
        {
            internal string Key = "";
            internal Transform Source = null!, Content = null!;
            internal Transform? CloneOf(Transform source) => source == Source ? Content : null;
        }
        internal Transform? Zone;
        internal List<Piece> Pieces = new();
        internal List<Inscription> Inscriptions = new();
        internal List<TownServiceSurface> Surfaces = new();
    }
    internal sealed class TownServiceToken
    {
        internal bool IsPhysical, IsMoving;
        internal Transform? HeldContent;
        internal Transform Source = null!;
        internal readonly Dictionary<Transform, Transform> HeldMap = new();
        internal Transform? HeldCloneOf(Transform source) => HeldMap.TryGetValue(source, out var copy) ? copy : null;
    }
    internal sealed class TownServiceTray { internal Transform Root = null!; }
    internal sealed class UITooltip : MonoBehaviour { internal Transform? m_AnchorToTarget; }
    internal static class NativeTemplates
    {
        internal static bool Ready = true;
        internal static UITooltip? Tooltip;
        internal static readonly Dictionary<string, Transform> Originals = new();
        internal static Transform? Original(string key) => Originals.TryGetValue(key, out var value) ? value : null;
        internal static string CardKey(AbilityCardUI card) => "card." + card.CardID;
        internal static string TooltipKey(UITooltip tooltip) => "tooltip.fixture";
    }
    internal static class TownServicePresentation
    {
        internal static bool Active = true;
        internal static byte Service = 1;
        internal static uint Session = 1200;
        internal static float SessionAge;
        internal static ulong RelocationRevision;
        internal static float RelocationVisibility = 1f;
        internal static PublisherWindow? Window;
        internal static TownServiceCatalog? Catalog;
        internal static TownServiceRitual? Ritual;
        internal static List<TownServiceWorkspace.Prop>? WorkspaceProps;
        internal static List<TownServiceSurface> LocalSurfaces = new();
        internal static List<TownServiceToken> Samples = new();
        internal static TownServiceTray? Tray;
        internal static Transform? CounterFurniture;
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
        private static readonly List<Transform> Dynamic = new(), RemovedSources = new(), PriorityRoots = new();
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
        internal static bool BindModules;
        internal static int GenerationReports;
        private static readonly HashSet<string> Registered = new();
        private static void Report(string scope, Exception error) { if (scope == "generation") GenerationReports++; }
        private static bool OwnsAnchor(Transform? anchor) => anchor != null;
        private static void Publish(string key, Transform? source, Transform? provenance = null, Func<Transform, Transform?>? cloneOf = null)
        {
            if (source == null) return;
            Calls.Add(new() { Key = key, Source = source, Provenance = provenance, CloneOf = cloneOf });
            Sources[source] = new() { Seen = true };
            string identity = key + "@" + source.GetInstanceID();
            if (!Modules.TryGetValue(identity, out Published? module)) Modules.Add(identity, module = new() { Id = ++_nextId });
            module.Seen = true;
            if (BindModules)
            {
                string address = key + "|";
                if (Registered.Add(address)) TownServiceMirror.RegisterTemplate(_service, 1, source, address: address);
                TownServiceMirror.RegisterModule(module.Id, 1, source, address: address);
            }
        }
    }
}
