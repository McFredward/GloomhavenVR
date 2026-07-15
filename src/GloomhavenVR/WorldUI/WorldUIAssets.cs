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

    /// <summary>Simple single-color material (game-shipped shaders only, no bundle dependency).</summary>
    internal static Material CreateFlatMaterial(Color color)
    {
        Shader? shader = Shader.Find("Sprites/Default")
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
            VRLog.Warn("WorldUI", $"AssetBundle.LoadFromFile failed for {bundlePath} — procedural table assets active.");
        return _bundle;
    }
}
