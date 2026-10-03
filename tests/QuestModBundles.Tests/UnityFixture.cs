using System.Text.Json;

namespace UnityEngine
{
    public static class Application { public static string unityVersion = "2021.3.5f1", dataPath; public static bool isBatchMode = true; }
    public enum ColorSpace { Gamma, Linear }
    public static class Debug { public static void Log(object message) { } public static void LogError(object message) { } }
    public static class JsonUtility
    { public static string ToJson(object value, bool pretty) => JsonSerializer.Serialize(value, new JsonSerializerOptions { IncludeFields = true, WriteIndented = pretty }); }
}
namespace UnityEngine.Rendering { public enum GraphicsDeviceType { OpenGLES3, Vulkan } }
namespace UnityEditor
{
    public enum BuildTarget { StandaloneWindows64, Android }
    public enum StereoRenderingPath { MultiPass, SinglePass }
    public static class PlayerSettings
    {
        public static UnityEngine.ColorSpace colorSpace;
        public static StereoRenderingPath stereoRenderingPath;
        public static bool useDefaultGraphics;
        public static UnityEngine.Rendering.GraphicsDeviceType[] graphicsApis;
        public static void SetUseDefaultGraphicsAPIs(BuildTarget target, bool value) => useDefaultGraphics = value;
        public static void SetGraphicsAPIs(BuildTarget target, UnityEngine.Rendering.GraphicsDeviceType[] value) => graphicsApis = value;
    }
    [Flags] public enum BuildAssetBundleOptions { None = 0, DisableWriteTypeTree = 1, ChunkBasedCompression = 2, ForceRebuildAssetBundle = 4, StrictMode = 8 }
    public static class EditorUserBuildSettings { public static BuildTarget activeBuildTarget = BuildTarget.Android; }
    public static class EditorApplication { public static int exitCode = -1; public static void Exit(int code) => exitCode = code; }
    public struct AssetBundleBuild { public string assetBundleName; public string[] assetNames; }
    public sealed class AssetBundleManifest { public string[] GetAllAssetBundles() => new[] { "gloomhavenvr.bundle" }; }
    public static class AssetDatabase
    {
        public static string extraDependency;
        public static string AssetPathToGUID(string path) => File.Exists(path) ? "audited-imported-asset" : "";
        public static string[] GetDependencies(string[] assets, bool recursive) => assets.Concat(new[] { "Resources/unity_builtin_extra" })
            .Concat(extraDependency == null ? Array.Empty<string>() : new[] { extraDependency }).ToArray();
    }
    public static class BuildPipeline
    {
        public static Action duringBuild;
        public static bool fail;
        public static BuildTarget lastTarget;
        public static BuildAssetBundleOptions lastOptions;
        public static AssetBundleBuild[] lastBuilds;
        public static AssetBundleManifest BuildAssetBundles(string output, AssetBundleBuild[] builds, BuildAssetBundleOptions options, BuildTarget target)
        {
            lastTarget = target; lastOptions = options; lastBuilds = builds;
            File.WriteAllBytes(Path.Combine(output, "gloomhavenvr.bundle"), new byte[] { 1, 2, 3, 4 });
            duringBuild?.Invoke(); return fail ? null : new AssetBundleManifest();
        }
    }
}
