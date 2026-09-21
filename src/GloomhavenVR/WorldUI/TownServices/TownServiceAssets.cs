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

    internal static GameObject? Prefab(string name)
        => Load<GameObject>("prefabs/" + name + ".prefab");

    internal static Shader? Shader(string name) => Load<Shader>("shaders/" + name + ".shader");
    internal static AudioClip? Audio(string name) => Load<AudioClip>("audio/" + name + ".wav");
    internal static TextAsset? Text(string name) => Load<TextAsset>("audio/" + name + ".json");

    // Speech may prepare before the first native service window opens. All asset types
    // share the same one-time probe and optional-install behavior as resident prefabs.
    private static T? Load<T>(string relativePath) where T : UnityEngine.Object
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
        return _bundle != null ? _bundle.LoadAsset<T>("assets/bundle/townservices/" + relativePath) : null;
    }

    internal static void Reset() { _bundle = null; _probed = false; }
}
