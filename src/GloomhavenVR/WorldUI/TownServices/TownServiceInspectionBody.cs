using System;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GloomhavenVR.Cards;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Immutable geometry addresses for the existing ItemChip backing. Geometry dimensions
/// are part of template identity, so another user's card-size preference cannot change its mesh.</summary>
internal static class TownServiceInspectionBody
{
    [StructLayout(LayoutKind.Explicit)]
    private struct FloatBits { [FieldOffset(0)] internal float Value; [FieldOffset(0)] internal int Bits; }
    private sealed class Address { internal string Key = string.Empty; }
    private static readonly ConditionalWeakTable<ItemsPile.ItemChip, Address> Addresses = new();
    internal static string Key(ItemsPile.ItemChip chip)
    {
        if (Addresses.TryGetValue(chip, out Address value)) return value.Key;
        MeshFilter? filter = chip.InspectionBody?.GetComponent<MeshFilter>();
        bool procedural = filter != null && filter.sharedMesh != null
            && filter.sharedMesh.name.StartsWith("GloomhavenVR.CardBody", StringComparison.Ordinal);
        string key = "inspectionbody." + new FloatBits { Value = chip.FaceWidth }.Bits.ToString("x8", CultureInfo.InvariantCulture)
            + "." + new FloatBits { Value = chip.FaceHeight }.Bits.ToString("x8", CultureInfo.InvariantCulture)
            + (procedural ? ".p" : ".b");
        Addresses.Add(chip, new Address { Key = key }); return key;
    }
    private static void Dimensions(string key, out float width, out float height, out bool prefab)
    {
        string[] parts = key.Split('.');
        if (parts.Length != 4 || parts[0] != "inspectionbody" || parts[3] != "p" && parts[3] != "b"
            || !int.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int w)
            || !int.TryParse(parts[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int h))
            throw new InvalidDataException("Invalid original item backing dimensions.");
        prefab = parts[3] == "b";
        width = new FloatBits { Bits = w }.Value; height = new FloatBits { Bits = h }.Value;
        if (!(width > 0 && width <= 2f && height > 0 && height <= 2f))
            throw new InvalidDataException("Original item backing dimensions exceed physical card bounds.");
    }
    internal static GameObject Create(string key, Transform parent)
    {
        Dimensions(key, out float width, out float height, out bool prefab);
        return ItemsPile.ItemChip.CreateInspectionBodyTemplate(parent, width, height, prefab)
            ?? throw new InvalidDataException("The original item backing is not available yet.");
    }
    internal static void RebindClone(string address, GameObject clone)
    {
        string key = address.Split('|')[0];
        if (!key.StartsWith("inspectionbody.", StringComparison.Ordinal)) return;
        Dimensions(key, out float width, out float height, out bool prefab);
        MeshFilter? filter = clone.GetComponent<MeshFilter>();
        // Prefab geometry stays exactly authored. Procedural original contours participate in
        // the same late silhouette completion registry as their local ItemChip source.
        if (!prefab && filter != null)
            CardMesh.AttachBody(filter, CardBodyKind.Item, width, height);
    }
}
