using System;
using GloomhavenVR.Cards;
using UnityEngine;
using UnityEngine.UI;
using Registry = GloomhavenVR.Net.TownServices.TownServiceAssets;

namespace GloomhavenVR.WorldUI;

/// <summary>Only the immutable original root backdrops of the native service templates.
/// The game ships two different Black_Backdrop images with identical descriptor metadata.
/// Merchant/enchantment use one, temple/confirmations the other. Neither pixel image may be
/// substituted for the other. Dynamic card/portrait art keeps its existing model-aware keys.</summary>
internal static class TownServiceBackdropAssets
{
    // Fixed order on every client, independent of which service is opened first. Shared texture
    // and sprite objects keep the first canonical provenance, never the last window scanned.
    private static readonly string[] Templates = { "merchant", "temple", "enchant", "item.confirm", "enhance.confirm" };

    internal static void Register(Registry assets, Func<string, Transform?> original)
    {
        foreach (string template in Templates)
        {
            Transform? root = original(template);
            Image? image = root != null ? root.GetComponent<Image>() : null;
            if (image == null || image.sprite == null) continue;
            Sprite sprite = CardFaceMipBake.OriginalFor(image.sprite);
            if (sprite.name != "Black_Backdrop" || sprite.texture.name != "Black_Backdrop") continue;
            // CardFaceMipBake returns the native sprite, whose texture remains the original.
            assets.RegisterOriginal("native-town|backdrop|" + template + "|texture", sprite.texture);
            assets.RegisterOriginal("native-town|backdrop|" + template + "|sprite", sprite);
        }
    }
}
