using UnityEngine;
namespace GloomhavenVR.Core
{
    internal static class VRLayers { internal const int ModLayer = 27; }
    internal static class VRLog
    {
        internal static int Warnings;
        internal static void Warn(string area, string message) { Warnings++; Debug.LogWarning(area + ": " + message); }
    }
}
namespace GloomhavenVR.WorldUI
{
    internal static class TownServiceAssets
    { internal static Shader Shader(string name) => UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/TownNpc.shader"); }
    internal static class TownLightingEditorLifetime
    {
        static readonly System.Collections.Generic.List<Object> Pending = new System.Collections.Generic.List<Object>();
        internal static void Destroy(Object value) => Pending.Add(value);
        internal static void Flush()
        { foreach (Object value in Pending) if (value != null) Object.DestroyImmediate(value); Pending.Clear(); }
    }
    internal static class SkyAlternative
    {
        internal static bool Moon;
        internal static bool TryRoomMoonDirection(out Vector3 direction, out Color colour, out float intensity)
        { direction = new Vector3(.493f,.643f,.587f); colour = new Color(.70f,.79f,.94f); intensity = .4f; return Moon; }
    }
}
namespace GloomhavenVR.Net.TownServices
{
    // Only asset lookup is a fixture boundary; mesh capture, every shader property,
    // material validation and inert material playback are actual production code.
    internal static class TownServiceFrame { internal const int MaxProperties=254; }
    internal sealed class TownServiceAssets
    {
        readonly System.Collections.Generic.Dictionary<string,Object> _assets=new System.Collections.Generic.Dictionary<string,Object>();
        readonly System.Collections.Generic.Dictionary<Object,string> _keys=new System.Collections.Generic.Dictionary<Object,string>();
        internal string Key(Object asset) { if(asset==null)return string.Empty;if(!_keys.TryGetValue(asset,out string key)){key="fixture-original-"+_keys.Count;_keys.Add(asset,key);_assets.Add(key,asset);}return key; }
        internal T Resolve<T>(string key) where T:Object => key.Length==0?null:(T)_assets[key];
    }
}
