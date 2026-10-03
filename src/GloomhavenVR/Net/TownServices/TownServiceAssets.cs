using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using GloomhavenVR.Cards;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GloomhavenVR.Net.TownServices;

/// <summary>Original asset references, never instance IDs or a guessed replacement picture.</summary>
internal sealed class TownServiceAssets
{
    private readonly Dictionary<string, Object> _assets = new(StringComparer.Ordinal);
    private readonly HashSet<string> _ambiguous = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> _keys = new();
    private readonly Dictionary<int, string> _originalKeys = new();
    private float _nextScan;
    internal uint Generation { get; private set; }

    internal void Clear()
    { _assets.Clear(); _keys.Clear(); _originalKeys.Clear(); _ambiguous.Clear(); _nextScan = 0; Generation++; }

    /// <summary>Immutable native-template provenance, installed in deterministic order before
    /// descriptor discovery. Reusing one original in another window retains its first identity;
    /// different originals must never alias just because their native names and sizes match.</summary>
    internal void RegisterOriginal(string key, Object asset)
    {
        if (asset == null || string.IsNullOrEmpty(key)) throw new ArgumentException("Missing original town-service asset.");
        int id = asset.GetInstanceID();
        if (_originalKeys.ContainsKey(id)) return;
        if (_assets.TryGetValue(key, out Object? other) && other != null && !ReferenceEquals(other, asset))
            throw new InvalidDataException("Conflicting original town-service provenance: " + key);
        _originalKeys.Add(id, key); _keys[id] = key; _assets[key] = asset;
    }

    internal void Register(string key, Object asset)
    {
        if (asset == null || string.IsNullOrEmpty(key)) throw new ArgumentException("Missing town-service asset.");
        // Broad discovery and TMP atlas registration must not replace verified original slots.
        if (_originalKeys.ContainsKey(asset.GetInstanceID())) return;
        _keys[asset.GetInstanceID()] = key;
        // This method is the adapter's explicit original/model-aware identity contract.
        if (!_assets.ContainsKey(key)) _assets.Add(key, asset);
    }

    internal string Key(Object? asset)
    {
        if (asset == null) return string.Empty;
        if (asset is Texture textureAsset) asset = GloomhavenVR.WorldUI.PanelMipBake.OriginalFor(textureAsset);
        if (_keys.TryGetValue(asset.GetInstanceID(), out string? cached)) return cached;
        string key;
        switch (asset)
        {
            case Sprite sprite:
                sprite = CardFaceMipBake.OriginalFor(sprite);
                Rect rect = sprite.rect; Vector2 pivot = sprite.pivot; Vector4 border = sprite.border;
                key = "sprite|" + Key(sprite.texture) + "|" + sprite.name + "|" + Numbers(rect.x, rect.y, rect.width, rect.height,
                    pivot.x, pivot.y, border.x, border.y, border.z, border.w, sprite.pixelsPerUnit);
                break;
            case Texture2D texture:
                // Player assets use a unique native descriptor; unverified collisions
                // are refused, and generated RenderTextures require an explicit binding.
                key = "texture|" + texture.name + "|" + texture.width + "|" + texture.height + "|" + (int)texture.format + "|" + texture.mipmapCount;
                break;
            case TMP_SpriteAsset sprites:
                key = "tmpsprite|" + sprites.name + "|" + Key(sprites.spriteSheet) + "|" + sprites.spriteCharacterTable.Count;
                break;
            case TMP_FontAsset font:
                key = "tmpfont|" + font.name + "|" + font.faceInfo.familyName + "|" + font.faceInfo.styleName
                    + "|" + font.atlasWidth + "|" + font.atlasHeight;
                // The font's original atlas is a serialized dependency with a deterministic index.
                // TMP may wrap it in a texture without a Unity import hash.
                if (font.atlasTextures != null)
                    for (int i = 0; i < font.atlasTextures.Length; i++)
                        if (font.atlasTextures[i] != null) Register(key + "|atlas|" + i, font.atlasTextures[i]);
                break;
            case Font legacy:
                key = "font|" + legacy.name + "|" + legacy.fontSize + "|" + string.Join(",", legacy.fontNames);
                break;
            case Shader shader:
                key = "shader|" + shader.name;
                break;
            default:
                throw new InvalidDataException("Unsupported town-service asset: " + asset.GetType().Name + " " + asset.name);
        }
        if (_assets.TryGetValue(key, out Object? other) && other != null && !ReferenceEquals(other, asset)
            && asset is Texture2D && !SameKnownNativeIdentity((Texture2D)asset))
        { _ambiguous.Add(key); throw new InvalidDataException("Ambiguous native town-service texture requires an explicit binding: " + asset.name); }
        Register(key, asset); return key;
    }

    internal T? Resolve<T>(string key) where T : Object
    {
        if (key.Length == 0) return null;
        if (_ambiguous.Contains(key)) throw new InvalidDataException("Ambiguous native town-service asset: " + key);
        if (!_assets.TryGetValue(key, out Object? asset) || asset == null)
        {
            Scan();
            if (!_assets.TryGetValue(key, out asset) || asset == null)
                throw new InvalidDataException("Original town-service asset is not loaded: " + key);
        }
        if (asset is not T typed) throw new InvalidDataException("Town-service asset type mismatch.");
        return typed;
    }

    internal void Scan()
    {
        if (Time.unscaledTime < _nextScan) return;
        _nextScan = Time.unscaledTime + 2;
        foreach (TMP_FontAsset font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>()) TryKey(font);
        foreach (TMP_SpriteAsset sprites in Resources.FindObjectsOfTypeAll<TMP_SpriteAsset>()) TryKey(sprites);
        foreach (Font font in Resources.FindObjectsOfTypeAll<Font>()) TryKey(font);
        foreach (Texture2D texture in Resources.FindObjectsOfTypeAll<Texture2D>()) TryKey(texture);
        foreach (Sprite sprite in Resources.FindObjectsOfTypeAll<Sprite>()) TryKey(sprite);
        foreach (Shader shader in Resources.FindObjectsOfTypeAll<Shader>()) TryKey(shader);
    }
    private void TryKey(Object asset)
    { try { Key(asset); } catch (InvalidDataException) { /* Unrelated transient assets are not part of this service. */ } }
    internal static bool SameKnownNativeIdentity(Texture2D texture)
    {
        // The game's resources.assets contains exactly one source object for each
        // of these names: T_noise_shards at path ID 405, AbilityCardSpriteAtlas at
        // 286 and Sarala-Regular SDF Atlas at 440. The latter two produced repeated
        // ambiguous-texture capture failures in the Build 587 multiplayer log,
        // dropping complete remote item/price modules while local cards stayed
        // readable. Unity can expose several wrappers for one serialized original.
        // Accept only the verified name, dimensions, format and mip count; unknown
        // same-name assets still fail closed instead of silently binding wrong art.
        return texture.name == "T_noise_shards" && texture.width == 512 && texture.height == 512
            && texture.format == TextureFormat.DXT1 && texture.mipmapCount == 10
            || texture.name == "AbilityCardSpriteAtlas" && texture.width == 2048 && texture.height == 2048
               && texture.format == TextureFormat.DXT5 && texture.mipmapCount == 1
            || texture.name == "Sarala-Regular SDF Atlas" && texture.width == 2048 && texture.height == 1024
               && texture.format == TextureFormat.Alpha8 && texture.mipmapCount == 1
            // Build-600 paired hardware logs identified five additional duplicate wrappers.
            // Read-only inspection across every original *.assets verified exactly one source
            // for each: resources.assets 205/261, sharedassets1.assets 89/625, and
            // sharedassets2.assets 51 respectively. Different dimensions/formats still fail.
            || texture.name == "MarcellusSC-Regular SDF Atlas" && texture.width == 2048 && texture.height == 4096
               && texture.format == TextureFormat.Alpha8 && texture.mipmapCount == 1
            || texture.name == "T_disc_ring" && texture.width == 512 && texture.height == 512
               && texture.format == TextureFormat.RGB24 && texture.mipmapCount == 10
            || texture.name == "Elementalist_ActiveAbility_Highlighted" && texture.width == 588 && texture.height == 91
               && texture.format == TextureFormat.RGBA32 && texture.mipmapCount == 1
            || texture.name == "T_flowmap_outwards_02" && texture.width == 512 && texture.height == 512
               && texture.format == TextureFormat.RGBA32 && texture.mipmapCount == 10
            // Build609's paired NPC logs identify duplicate runtime wrappers for these
            // three originals. Read-only inspection of EVERY original *.assets found
            // exactly one source each: sharedassets1/181, sharedassets2/131 and
            // sharedassets4/44. Their complete native descriptors are the identity;
            // neither visit order nor a generated template slot may name these assets.
            // HeroHighlight_Darken also has exactly one Sprite (sharedassets2/247).
            // Its canonical texture identity makes that original hover sprite resolve
            // on the other peer even when their borrowed item rows arrive in another order.
            || texture.name == "T_Noise_Spherical_Sparks" && texture.width == 512 && texture.height == 512
               && texture.format == TextureFormat.RGB24 && texture.mipmapCount == 10
            || texture.name == "HeroHighlight_Darken" && texture.width == 300 && texture.height == 218
               && texture.format == TextureFormat.RGBA32 && texture.mipmapCount == 1
            || texture.name == "T_flowmap_outwards" && texture.width == 1024 && texture.height == 1024
               && texture.format == TextureFormat.DXT5 && texture.mipmapCount == 11
            || texture.name == "CoinIcon2_White" && texture.width == 128 && texture.height == 128
               && texture.format == TextureFormat.DXT5 && texture.mipmapCount == 1
            || texture.name.StartsWith("sactx-", StringComparison.Ordinal)
               && texture.name.Contains("BattleOverlayCanvas-");
    }
    private static string Numbers(params float[] values)
    {
        var text = new string[values.Length];
        for (int i = 0; i < values.Length; i++) text[i] = values[i].ToString("R", CultureInfo.InvariantCulture);
        return string.Join(",", text);
    }
}
