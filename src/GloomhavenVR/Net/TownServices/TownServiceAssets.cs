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
    private float _nextScan;

    internal void Clear() { _assets.Clear(); _keys.Clear(); _ambiguous.Clear(); _nextScan = 0; }

    internal void Register(string key, Object asset)
    {
        if (asset == null || string.IsNullOrEmpty(key)) throw new ArgumentException("Missing town-service asset.");
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
                // imageContentsHash is editor-only. Player assets use a unique native descriptor;
                // collisions are refused, and generated RenderTextures require an explicit binding.
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
            && asset is Texture2D)
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
    private static string Numbers(params float[] values)
    {
        var text = new string[values.Length];
        for (int i = 0; i < values.Length; i++) text[i] = values[i].ToString("R", CultureInfo.InvariantCulture);
        return string.Join(",", text);
    }
}
