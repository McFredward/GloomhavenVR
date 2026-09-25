using System;
using System.Collections.Generic;

namespace GloomhavenVR.Net.TownServices;

/// <summary>Owner-authored presentation only. No field identifies a gameplay command.</summary>
internal sealed class TownServiceFrame
{
    // A complete late-game stock uses independent native card, price and physical-body
    // modules. Keep payloads bounded; only the manifest's ushort ID census grows (8 KiB).
    internal const int MaxBytes = 60000, MaxNodes = 256, MaxProperties = 128, MaxModules = 4096;
    internal bool PublicCatalog;
    internal uint PublicClaim;
    internal byte Service;
    internal const ushort ManifestModule = ushort.MaxValue, BundleStream = ushort.MaxValue - 1;
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
    // Quantized 8-byte edge controls per runner. Private furniture uses one
    // enchantress runner or two temple runners; null means no cloth record.
    internal byte[]? WorkspaceCloth;
    internal bool HasWorkspaceCloth => WorkspaceCloth != null;
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
