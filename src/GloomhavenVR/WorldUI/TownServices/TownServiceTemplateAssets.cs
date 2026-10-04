using System;
using System.Reflection;
using GloomhavenVR.Cards;
using GloomhavenVR.Net.TownServices;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using NativeAssetRegistry = GloomhavenVR.Net.TownServices.TownServiceAssets;

namespace GloomhavenVR.WorldUI;

/// <summary>Bind immutable native presentation dependencies by original template/slot provenance.
/// Descriptor discovery cannot distinguish different originals with the same name, and a TMP
/// material can be sampled before its font. No game controller or source material is modified.</summary>
internal static class TownServiceTemplateAssets
{
    /// <summary>The native UIInfoTools serialized field is the sprite identity, not its
    /// runtime packed atlas/UV descriptor. PCVR peers in Build612 exposed BC1 and BC3 atlas
    /// wrappers with different names; both still reference the same authored condition icon.
    /// Read only direct Sprite fields, Sprite arrays and the native nested EffectInfo value.
    /// No controller, getter, object graph or Addressables request is run here.</summary>
    internal static void RegisterSpriteCatalog(NativeAssetRegistry assets, object catalog)
    {
        Type type = catalog.GetType();
        if (type.Name != "UIInfoTools") throw new ArgumentException("Expected original UIInfoTools sprite catalogue.");
        FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Array.Sort(fields, (a, b) => string.CompareOrdinal(a.Name, b.Name));
        foreach (FieldInfo field in fields)
        {
            string key = "native-town|sprite-catalog|UIInfoTools/" + field.Name;
            if (field.FieldType == typeof(Sprite)) CatalogSprite(assets, key, field.GetValue(catalog) as Sprite);
            else if (field.FieldType == typeof(Sprite[]) && field.GetValue(catalog) is Sprite[] sprites)
            {
                if (sprites.Length > 1024) throw new InvalidOperationException("Native sprite catalogue exceeds bounded presentation capacity.");
                for (int i = 0; i < sprites.Length; i++) CatalogSprite(assets, key + "/" + i, sprites[i]);
            }
            else if (field.FieldType.DeclaringType == type && field.FieldType.Name == "EffectInfo")
            {
                object? effect = field.GetValue(catalog);
                foreach (FieldInfo icon in field.FieldType.GetFields(BindingFlags.Instance | BindingFlags.Public))
                    if (icon.FieldType == typeof(Sprite)) CatalogSprite(assets, key + "/" + icon.Name, icon.GetValue(effect) as Sprite);
            }
        }
    }

    private static void CatalogSprite(NativeAssetRegistry assets, string key, Sprite? sprite)
    {
        if (sprite != null) assets.RegisterOriginal(key, CardFaceMipBake.OriginalFor(sprite));
    }
    private static readonly string[] Templates =
    {
        "merchant.row", "merchant.tooltip", "merchant", "temple.row", "temple.tooltip", "temple",
        "enchant.row", "enchant.tooltip", "enchant.point", "enchant.highlight", "enchant",
        "item.confirm", "enhance.confirm", "merchant.zone", "merchant.crank", "merchant.rack",
        "merchant.counter", "temple.counter", "enchant.counter", "map.cardbody"
    };

    internal static void Register(NativeAssetRegistry assets, Func<string, Transform?> original)
    {
        foreach (string key in Templates)
        {
            Transform? root = original(key);
            if (root != null) Register(assets, key, root);
        }
    }

    internal static void Register(NativeAssetRegistry assets, string template, Transform root)
    {
        // Lazy model faces can be borrowed in different orders on different clients. Their
        // artwork keeps the model-aware/native resource identities, not the first borrower's.
        bool modelFace = template.StartsWith("item.", StringComparison.Ordinal)
            || template.StartsWith("face.", StringComparison.Ordinal)
            || template.StartsWith("inspectionbody.", StringComparison.Ordinal);
        // The order and paths are authored prefab identities, never instance IDs or visit order.
        Visit(assets, root, "native-town|template|" + template, root, modelFace);
    }

    private static void Visit(NativeAssetRegistry assets, Transform node, string path, Transform root, bool modelFace)
    {
        foreach (TMP_Text text in node.GetComponents<TMP_Text>())
        {
            if (text.font != null)
            {
                assets.Key(text.font);
                Texture2D[] atlases = text.font.atlasTextures;
                if (atlases != null)
                    for (int i = 0; i < atlases.Length; i++)
                        if (atlases[i] != null && !NativeAssetRegistry.SameKnownNativeIdentity(atlases[i]))
                            assets.RegisterOriginal(path + "|font-atlas|" + i, atlases[i]);
            }
            Material(assets, path + "|text", text.fontSharedMaterial);
        }
        foreach (Image image in node.GetComponents<Image>())
        {
            if (!modelFace)
            { Sprite(assets, path + "|image", image.sprite); Sprite(assets, path + "|override", image.overrideSprite); }
            Material(assets, path + "|graphic", image.material);
        }
        foreach (RawImage image in node.GetComponents<RawImage>())
        {
            if (!modelFace) Texture(assets, path + "|raw", image.texture);
            Material(assets, path + "|graphic", image.material);
        }
        foreach (Selectable selectable in node.GetComponents<Selectable>())
        {
            if (modelFace) continue;
            SpriteState state = selectable.spriteState;
            Sprite(assets, path + "|highlight", state.highlightedSprite);
            Sprite(assets, path + "|pressed", state.pressedSprite);
            Sprite(assets, path + "|selected", state.selectedSprite);
            Sprite(assets, path + "|disabled", state.disabledSprite);
        }
        foreach (Renderer renderer in node.GetComponents<Renderer>())
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++) Material(assets, path + "|mesh|" + i, materials[i]);
        }
        for (int i = 0; i < node.childCount; i++)
        {
            Transform child = node.GetChild(i);
            // Pooled gameplay rows/cards are separate model-aware originals registered on freeze.
            if (child != root && NativeTemplates.IsBoundary(child)) continue;
            Visit(assets, child, path + "/" + NativeTemplates.Append(string.Empty, child), root, modelFace);
        }
    }

    private static void Sprite(NativeAssetRegistry assets, string key, Sprite? sprite)
    {
        if (sprite == null) return;
        sprite = CardFaceMipBake.OriginalFor(sprite);
        if (NativeAssetRegistry.SameKnownNativeIdentity(sprite.texture)) { assets.Key(sprite); return; }
        Texture(assets, key + "|texture", sprite.texture);
        assets.RegisterOriginal(key + "|sprite", sprite);
    }

    private static void Texture(NativeAssetRegistry assets, string key, Texture? texture)
    {
        if (texture == null) return;
        texture = PanelMipBake.OriginalFor(texture);
        if (texture is Texture2D known && NativeAssetRegistry.SameKnownNativeIdentity(known)) assets.Key(known);
        else assets.RegisterOriginal(key, texture);
    }

    private static void Material(NativeAssetRegistry assets, string key, Material? material)
    {
        if (material == null || material.shader == null) return;
        Shader shader = material.shader;
        assets.Key(shader);
        for (int i = 0; i < shader.GetPropertyCount(); i++)
            if (shader.GetPropertyType(i) == ShaderPropertyType.Texture)
                Texture(assets, key + "|" + shader.GetPropertyName(i), material.GetTexture(shader.GetPropertyName(i)));
    }
}
