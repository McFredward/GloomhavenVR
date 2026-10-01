using System;
using System.Collections.Generic;
using System.IO;

namespace GloomhavenVR.Net.TownServices;

/// <summary>Full public cabinet placement, including cold pages. The stock author
/// retains its physical slots across unlocks; another peer's native pool history
/// must not move those items when that peer takes over the cabinet.</summary>
internal readonly struct TownCatalogSlot
{
    internal readonly int ItemId;
    // Globally unique: category * SlotsPerCategory + category-local ordinal.
    internal readonly ushort Ordinal;
    internal TownCatalogSlot(int itemId, ushort ordinal) { ItemId = itemId; Ordinal = ordinal; }
}

internal static class TownCatalogLayout
{
    internal const int MaxLayout = 384;
    internal const int SlotsPerCategory = 256 * 12;
    internal const int Categories = 6;

    internal static void Validate(TownCatalogSlot[] layout)
    {
        if (layout == null || layout.Length > MaxLayout)
            throw new InvalidDataException("Public cabinet layout exceeds its native item limit.");
        int previous = 0;
        var occupied = new HashSet<ushort>();
        foreach (TownCatalogSlot slot in layout)
        {
            if (slot.ItemId <= previous || slot.Ordinal >= Categories * SlotsPerCategory
                || !occupied.Add(slot.Ordinal))
                throw new InvalidDataException("Invalid public cabinet item placement.");
            previous = slot.ItemId;
        }
    }

    internal static bool Same(TownCatalogSlot[]? left, TownCatalogSlot[]? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left == null || right == null || left.Length != right.Length) return false;
        for (int i = 0; i < left.Length; i++)
            if (left[i].ItemId != right[i].ItemId || left[i].Ordinal != right[i].Ordinal) return false;
        return true;
    }

    internal static byte[] Write(TownCatalogSlot[] layout)
    {
        Validate(layout);
        using var bytes = new MemoryStream();
        using var writer = new BinaryWriter(bytes);
        writer.Write((byte)1); writer.Write((ushort)layout.Length);
        foreach (TownCatalogSlot slot in layout) { writer.Write(slot.ItemId); writer.Write(slot.Ordinal); }
        return bytes.ToArray();
    }

    internal static TownCatalogSlot[] Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, false);
        using var reader = new BinaryReader(stream);
        if (reader.ReadByte() != 1) throw new InvalidDataException("Unknown public cabinet layout version.");
        int count = reader.ReadUInt16();
        if (count > MaxLayout || stream.Length - stream.Position != count * 6)
            throw new InvalidDataException("Invalid public cabinet layout length.");
        var layout = new TownCatalogSlot[count];
        for (int i = 0; i < count; i++) layout[i] = new TownCatalogSlot(reader.ReadInt32(), reader.ReadUInt16());
        Validate(layout);
        return layout;
    }
}
