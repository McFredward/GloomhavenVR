using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Separate optional-at-runtime art bank; an incomplete install retains native services.
/// The name deliberately avoids the historical main-bundle substring discovery contract.</summary>
internal static class TownServiceAssets
{
    internal const string BundleName = "ghvr-town.bundle";
    private static AssetBundle? _bundle;
    private static bool _probed;

    private static AssetBundle? Bundle()
    {
        if (!_probed)
        {
            _probed = true;
            foreach (AssetBundle bundle in AssetBundle.GetAllLoadedAssetBundles())
                if (bundle != null && String.Equals(bundle.name, BundleName, StringComparison.OrdinalIgnoreCase))
                { _bundle = bundle; break; }
            if (_bundle == null)
            {
                string path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, BundleName);
                if (File.Exists(path)) _bundle = AssetBundle.LoadFromFile(path);
            }
        }
        return _bundle;
    }

    internal static GameObject? Prefab(string name) => Bundle()?.LoadAsset<GameObject>(
        "assets/bundle/townservices/prefabs/" + name + ".prefab");

    internal static Shader? Shader(string name) => Bundle()?.LoadAsset<Shader>(
        "assets/bundle/townservices/shaders/" + name + ".shader");

    internal static AudioClip? Audio(string name) => Bundle()?.LoadAsset<AudioClip>(
        "assets/bundle/townservices/audio/" + name + ".wav");

    internal static TextAsset? Text(string name) => Bundle()?.LoadAsset<TextAsset>(
        "assets/bundle/townservices/audio/" + name + ".json");

    internal static void Reset() { _bundle = null; _probed = false; }
}
