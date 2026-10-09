using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using GloomhavenVR.Cards;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>The exact local VRCard backing factory, addressed by its owner's
/// original dimensions and geometry family. No observer card-size setting is read.</summary>
internal static class TownServiceAbilityBody
{
    [StructLayout(LayoutKind.Explicit)]
    private struct FloatBits { [FieldOffset(0)] internal float Value; [FieldOffset(0)] internal int Bits; }

    internal static string Key(VRCard card)
    {
        Vector2 size = card.OriginalBackingSize;
        return "map.cardbody." + new FloatBits { Value = size.x }.Bits.ToString("x8", CultureInfo.InvariantCulture)
            + "." + new FloatBits { Value = size.y }.Bits.ToString("x8", CultureInfo.InvariantCulture)
            + (card.HasProceduralBacking ? ".p" : ".b");
    }

    private static void Dimensions(string key, out float width, out float height, out bool procedural)
    {
        string[] parts = key.Split('.');
        if (parts.Length != 5 || parts[0] != "map" || parts[1] != "cardbody"
            || parts[4] != "p" && parts[4] != "b"
            || !int.TryParse(parts[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int w)
            || !int.TryParse(parts[3], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int h))
            throw new InvalidDataException("Invalid original ability backing dimensions.");
        procedural = parts[4] == "p";
        width = new FloatBits { Bits = w }.Value; height = new FloatBits { Bits = h }.Value;
        if (!(width > 0f && width <= 2f && height > 0f && height <= 2f))
            throw new InvalidDataException("Original ability backing dimensions exceed physical card bounds.");
    }

    internal static GameObject Create(string key, Transform parent)
    {
        Dimensions(key, out float width, out float height, out bool procedural);
        // Build658 repeatedly refused map.cardbody when the optional bundled
        // prefab was absent, although the local VRCard had successfully built its
        // normal procedural original. Use the very same factory and materials;
        // this is that owner's original body, not a replacement for missing art.
        if (procedural) return VRCard.BuildProceduralBacking(parent, width, height).gameObject;
        GameObject? original = CardsDriver.CardBackingPrefab
            ?? WorldUIAssets.TryLoadPrefab("Assets/Bundle/Table/CardBacking.prefab");
        if (original == null) throw new InvalidDataException("The original bundled ability backing is not available yet.");
        GameObject body = UnityEngine.Object.Instantiate(original, parent, false);
        body.name = "Backing"; return body;
    }

    internal static void RebindClone(string address, GameObject clone)
    {
        string key = address.Split('|')[0];
        if (!key.StartsWith("map.cardbody.", StringComparison.Ordinal)) return;
        Dimensions(key, out float width, out float height, out bool procedural);
        if (!procedural) return;
        MeshFilter? filter = clone.GetComponent<MeshFilter>();
        if (filter == null) throw new InvalidDataException("The original ability backing has no mesh filter.");
        // Instantiate copies the current mesh but not the original late contour
        // consumer. Keep the frozen and observer bodies in the same live registry.
        CardMesh.AttachBody(filter, CardBodyKind.Ability, width, height);
    }
}
