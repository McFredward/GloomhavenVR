using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GloomhavenVR.Core;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Asset access for WorldUI: probes <c>gloomhavenvr.bundle</c> for table prefabs
/// (see <c>unity/GloomhavenVR.Assets/Assets/Bundle/Table/README.md</c>) with
/// procedural fallbacks, plus small material/font helpers.
///
/// The bundle may already be loaded by the Hands module (same file) — Unity refuses
/// a second <c>LoadFromFile</c> on a loaded bundle, so the loaded-bundle registry is
/// checked first.
/// </summary>
internal static class WorldUIAssets
{
    private const string BundleFileName = "gloomhavenvr.bundle";

    private static AssetBundle? _bundle;
    private static bool _probed;
    private static TMP_FontAsset? _gameFont;

    internal static GameObject? TryLoadPrefab(string assetPath)
    {
        AssetBundle? bundle = GetBundle();
        return bundle != null ? bundle.LoadAsset<GameObject>(assetPath) : null;
    }

    private static readonly Dictionary<string, Sprite?> _sprites = new(4);

    /// <summary>
    /// A bundled PNG as a uGUI <see cref="Sprite"/>, built at RUNTIME from the texture rather than
    /// imported as a sprite in the editor. That is deliberate: a sprite import is a per-asset
    /// setting in the companion project that nothing checks, and one that silently reverts to
    /// "Default" ships a texture no <c>Image</c> can draw. Building it here needs no import
    /// configuration at all, so it cannot be got wrong in a place the build does not look.
    /// Cached, including the miss, because a failed lookup asked every frame is still a lookup.
    /// </summary>
    internal static Sprite? TryLoadSprite(string assetPath)
    {
        if (_sprites.TryGetValue(assetPath, out Sprite? cached))
            return cached;
        Sprite? sprite = null;
        AssetBundle? bundle = GetBundle();
        var texture = bundle != null ? bundle.LoadAsset<Texture2D>(assetPath) : null;
        if (texture != null)
        {
            sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                                   new Vector2(0.5f, 0.5f), 100f);
            sprite.name = assetPath;
        }
        else
        {
            VRLog.Warn("WorldUI", $"{assetPath} is not in the bundle — whatever asked for it "
                + "degrades without it. (A bundle older than the asset does this.)");
        }
        _sprites[assetPath] = sprite;
        return sprite;
    }

    /// <summary>
    /// Simple single-color material. Default path uses game-shipped shaders only (no
    /// bundle dependency). When <paramref name="overlay"/> is set (item 9), the bundled
    /// <c>GloomhavenVR/Overlay</c> shader is used instead — it EXPOSES <c>_ZTest</c>/
    /// <c>_ZWrite</c> so the renderer can be forced to draw OVER the opaque control
    /// board (a plain <c>Sprites/Default</c> material has no <c>_ZTest</c> and cannot).
    /// Falls back to the flat shaders if the bundle (hence the Overlay shader) is absent.
    /// </summary>
    internal static Material CreateFlatMaterial(Color color, bool overlay = false)
    {
        // Load the bundled Overlay shader via the bundle (Shader.Find can't see a bundled
        // shader nothing has loaded — see Cards.PlayTray.OverlayShader).
        Shader? shader = overlay ? Cards.PlayTray.OverlayShader() : null;
        shader ??= Shader.Find("Sprites/Default")
                   ?? Shader.Find("Legacy Shaders/Diffuse")
                   ?? Shader.Find("Hidden/InternalErrorShader");
        var material = new Material(shader) { color = color };
        return material;
    }

    /// <summary>
    /// Give a world TMP label the same font the game's HUD uses (guaranteed loaded),
    /// harvested lazily from the live ReadyButton label.
    /// </summary>
    internal static void TryAssignGameFont(TMP_Text label)
    {
        if (_gameFont == null)
        {
            Choreographer choreographer = Choreographer.s_Choreographer;
            if (choreographer != null && choreographer.readyButton != null
                && choreographer.readyButton.buttonText != null)
            {
                _gameFont = choreographer.readyButton.buttonText.font;
            }
        }
        if (_gameFont != null && label.font != _gameFont)
            label.font = _gameFont;
    }

    internal static void Reset()
    {
        // The bundle itself may be owned by the Hands module — never unload here,
        // just drop our references (hot-reload safe).
        _bundle = null;
        _probed = false;
        _gameFont = null;
    }

    private static AssetBundle? GetBundle()
    {
        if (_probed)
            return _bundle;
        _probed = true;

        // Already loaded (by Hands)?
        foreach (AssetBundle loaded in AssetBundle.GetAllLoadedAssetBundles())
        {
            if (loaded != null && loaded.name.Contains("gloomhavenvr"))
            {
                _bundle = loaded;
                return _bundle;
            }
        }

        string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        string bundlePath = Path.Combine(pluginDir, BundleFileName);
        if (!File.Exists(bundlePath))
            return null;

        _bundle = AssetBundle.LoadFromFile(bundlePath);
        if (_bundle == null)
            VRLog.Warn("WorldUI", $"AssetBundle.LoadFromFile failed for {bundlePath} — procedural table assets active. " +
                                  $"Cause: {BundleDiagnostics.Explain(bundlePath)}");
        return _bundle;
    }
}
