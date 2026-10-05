using System;
using System.Collections.Generic;

namespace GloomhavenVR.Net.TownServices;

/// <summary>Owner-authored presentation only. No field identifies a gameplay command.</summary>
internal sealed class TownServiceFrame
{
    // A complete late-game stock uses independent native card, price and physical-body
    // modules. Keep payloads bounded; only the manifest's ushort ID census grows (8 KiB).
    // A material records each shader property as a name/texture pair. The former 128-text
    // limit admitted only 63 shader properties and silently excluded real item-card
    // materials from every multiplayer snapshot. The codec stores the count in a byte;
    // 254 entries permit 126 complete properties without changing its wire grammar.
    internal const int MaxBytes = 60000, MaxNodes = 256, MaxProperties = 254, MaxModules = 4096;
    internal bool PublicCatalog;
    // Separate cosmetic lane: taking a sample must survive public page authorship
    // changes and visiting another NPC without occupying its interaction lease.
    internal bool VisitorStock;
    internal uint PublicClaim;
    internal byte Service;
    internal const ushort ManifestModule = ushort.MaxValue, BundleStream = ushort.MaxValue - 1,
        VoiceModule = ushort.MaxValue - 2, UrgentBundleStream = ushort.MaxValue - 4;
    internal const string VoiceAddress = "town.voice.reaction|v1";
    internal uint Session;
    internal ulong Sequence;
    internal ulong BaseSequence;
    internal ushort Module, Template;
    internal string TemplateAddress = string.Empty;
    internal ushort ParentModule = ManifestModule;
    internal uint ParentBinding;
    internal uint Structure;
    internal bool Visible;
    internal TownRackState? Rack;
    internal TownRackStamp? RackMember;
    // Additive TLV102: exact original content required by this public rack clock.
    internal TownCatalogBank? CatalogBank;
    // Additive TLV105: sparse original properties against this client's immutable
    // native prefab. Never a prior network sample or a gameplay model mutation.
    internal ulong NativeTemplateBasisKey;
    // Reserved TLV90 legacy grammar. Build 582 authors null, so ordinary furniture
    // snapshots contain no simulated fabric controls; old vectors remain parseable.
    internal byte[]? WorkspaceCloth;
    internal bool HasWorkspaceCloth => WorkspaceCloth != null;
    // Additive TLV91 lives on a private service manifest. It carries no
    // transaction or character identity: it only says whether the current temple
    // interaction owner sampled the native donation affordance as available.
    internal bool TempleDonationKnown;
    internal bool TempleDonationAvailable;
    internal uint TempleDonationRevision;
    // Additive TLV93 preserves the committed blessing age across transport/late join.
    internal bool HasTempleDonationCommitAge;
    internal float TempleDonationCommitAge;
    // Additive TLV92 belongs only to a private active manifest. A visitor may browse a
    // service without reserving it; the claim begins only after an offer is placed.
    internal bool TransactionActive;
    // Local transport scheduling only; never serialized or interpreted as gameplay authority.
    internal bool HighPriority;
    internal float SampleTime;
    internal float SessionAge;
    internal float ParentAlpha = 1f;
    internal ushort[] Modules = Array.Empty<ushort>();
    // Position, quaternion and scale in the shared map frame; never observer-local layout.
    internal float[] Pose = new float[10];
    // A converted/held root can sit inside an original external canvas whose pixel frame
    // differs from the root's own animation scale. Retain that frame for clipping and softness.
    internal bool HasCanvasFrame;
    internal float[] CanvasPose = new[] { 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f };
    internal float[] CanvasRect = new[] { 100f, 100f, .5f, .5f };
    internal float[] CanvasSettings = new[] { 100f, 0f, 0f, 1f, 0f };
    internal int CanvasSortingOrder, CanvasSortingLayer;
    internal TownServiceNode[] Nodes = Array.Empty<TownServiceNode>();
}

/// <summary>Local completion of a snapshot's final datagram, never a remote acknowledgement.</summary>
internal static class TownServiceDelivery
{
    internal static Action<TownServiceFrame>? Completed = null;
    private sealed class RetiredLane
    {
        internal byte Service;
        internal uint Session;
        internal bool Pending;
        // Module IDs are ushort and are never reused during a source session.
        // A fixed bitmap also bounds retirement while transport is offline.
        internal readonly byte[] Modules = new byte[8192];
    }
    private static readonly RetiredLane[] Retired = { new(), new(), new() };
    private static int Lane(bool publicCatalog, bool visitorStock) => visitorStock ? 2 : publicCatalog ? 1 : 0;
    internal static void Retire(bool publicCatalog, bool visitorStock, byte service, uint session, ushort module)
    {
        RetiredLane lane = Retired[Lane(publicCatalog, visitorStock)];
        if (lane.Service != service || lane.Session != session)
        { Array.Clear(lane.Modules, 0, lane.Modules.Length); lane.Service = service; lane.Session = session; }
        lane.Modules[module >> 3] |= (byte)(1 << (module & 7)); lane.Pending = true;
    }
    internal static bool HasRetired(bool publicCatalog, bool visitorStock, byte service, uint session)
    { RetiredLane lane = Retired[Lane(publicCatalog, visitorStock)]; return lane.Pending && lane.Service == service && lane.Session == session; }
    internal static bool IsRetired(bool publicCatalog, bool visitorStock, ushort module)
    { RetiredLane lane = Retired[Lane(publicCatalog, visitorStock)]; return (lane.Modules[module >> 3] & (1 << (module & 7))) != 0; }
    // The single production FFS presentation scheduler consumes all three lanes
    // together on the Unity thread. No subscriber or native object is retained.
    internal static void ClearRetired()
    { foreach (RetiredLane lane in Retired) if (lane.Pending) { Array.Clear(lane.Modules, 0, lane.Modules.Length); lane.Pending = false; } }
}

internal sealed class TownServiceNode
{
    internal uint Binding;
    internal readonly Dictionary<ushort, TownServiceValue> Values = new();
}

/// <summary>Small typed property vocabulary shared by the sampler and inert playback.</summary>
internal sealed class TownServiceValue
{
    internal float[] Numbers = Array.Empty<float>();
    internal string[] Text = Array.Empty<string>();
    internal bool Same(TownServiceValue other)
    {
        if (Numbers.Length != other.Numbers.Length || Text.Length != other.Text.Length) return false;
        for (int i = 0; i < Numbers.Length; i++) if (Numbers[i] != other.Numbers[i]) return false;
        for (int i = 0; i < Text.Length; i++) if (Text[i] != other.Text[i]) return false;
        return true;
    }
}

internal sealed class TownServiceValueComparer : IEqualityComparer<TownServiceValue>
{
    internal static readonly TownServiceValueComparer Instance = new();
    public bool Equals(TownServiceValue? x, TownServiceValue? y) => ReferenceEquals(x, y) || (x != null && y != null && x.Same(y));
    public int GetHashCode(TownServiceValue value)
    {
        int hash = 17;
        foreach (float number in value.Numbers) hash = unchecked(hash * 31 + number.GetHashCode());
        foreach (string text in value.Text) hash = unchecked(hash * 31 + StringComparer.Ordinal.GetHashCode(text));
        return hash;
    }
}

internal static class TownServiceProperty
{
    internal const ushort Transform = 1, Active = 2, Graphic = 3, Renderer = 4, Group = 5,
        Image = 6, RawImage = 7, TmpText = 8, LegacyText = 9, Mask = 10, RectMask = 11,
        Material = 12, TextMaterial = 13, Shadow = 14, Outline = 15, Canvas = 16, Sibling = 17,
        Mesh = 18, MeshMaterial0 = 19, MeshMaterial7 = 26;
    internal const ushort Last = MeshMaterial7;
}
