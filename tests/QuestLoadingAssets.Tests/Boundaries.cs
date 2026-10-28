namespace UnityEngine.Rendering { public enum GraphicsDeviceType { Vulkan, OpenGLCore } }
namespace UnityEngine.XR
{
    public static class XRSettings
    {
        public enum StereoRenderingMode { MultiPass, SinglePassInstanced }
        public static StereoRenderingMode stereoRenderingMode = StereoRenderingMode.MultiPass;
    }
}
namespace UnityEngine
{
    public static class SystemInfo { public static Rendering.GraphicsDeviceType graphicsDeviceType = Rendering.GraphicsDeviceType.Vulkan; }
    public sealed class Shader { public string name = "GloomhavenVR/QuestLoadingSpinner"; public bool isSupported = true; }
    // Native Android AssetBundle/Shader APIs are explicit modeled boundaries.
    // Actual embedded bytes, provenance hashing/parser, gate, cache and fallback execute.
    public sealed class AssetBundle
    {
        public static string Mode = "success";
        public static int Loads, Unloads;
        public string name = "ghvr-quest-loading.bundle";
        public static AssetBundle? LoadFromMemory(byte[] bytes)
        {
            Loads++;
            if (Mode == "load-null") return null;
            return new AssetBundle { name = Mode == "wrong-bank" ? "foreign.bundle" : "ghvr-quest-loading.bundle" };
        }
        public string[] GetAllAssetNames() => Mode == "wrong-path" ? new[] { "foreign.shader" } : new[] { "assets/questloading/questloadingspinner.shader" };
        public T? LoadAsset<T>(string path) where T : class
        {
            if (Mode == "missing-shader") return null;
            return new Shader { name = Mode == "wrong-shader" ? "Foreign/Shader" : "GloomhavenVR/QuestLoadingSpinner", isSupported = Mode != "unsupported" } as T;
        }
        public void Unload(bool all) { Unloads++; }
    }
}
namespace GloomhavenVR.Core
{
    internal static class QuestStandalonePlatform { internal static bool Enabled; }
    internal static class VRLog
    {
        internal static readonly List<string> Notes = new();
        internal static void Note(string scope, string message) => Notes.Add(message);
    }
}
