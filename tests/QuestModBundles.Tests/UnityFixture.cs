using System.Text.Json;

namespace UnityEngine
{
    public class Object { public string name; }
    public sealed class AudioClip : Object { }
    public sealed class TextAsset : Object { }
    public sealed class Mesh : Object { }
    public sealed class MeshFilter : Object { public Mesh sharedMesh = new Mesh(); }
    public sealed class Transform : Object
    {
        public readonly List<Transform> children = new();
        public readonly List<MeshFilter> meshFilters = new();
        public Transform Find(string childName) => children.FirstOrDefault(child => child.name == childName);
        public T[] GetComponentsInChildren<T>(bool includeInactive)
        {
            var transforms = new[] { this }.Concat(children.SelectMany(child => child.GetComponentsInChildren<Transform>(includeInactive)));
            return typeof(T) == typeof(Transform) ? transforms.Cast<T>().ToArray()
                : typeof(T) == typeof(MeshFilter) ? transforms.SelectMany(child => child.meshFilters).Cast<T>().ToArray()
                : Array.Empty<T>();
        }
    }
    public sealed class GameObject : Object
    {
        public readonly Transform transform = new();
        public T[] GetComponentsInChildren<T>(bool includeInactive) => transform.GetComponentsInChildren<T>(includeInactive);
    }
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
    public sealed class AssetBundleManifest
    {
        public string[] GetAllAssetBundles() => BuildPipeline.manifestOverride ?? BuildPipeline.lastBuilds.Select(build => build.assetBundleName).ToArray();
        public string[] GetAllDependencies(string bundle) => BuildPipeline.bundleDependencies.TryGetValue(bundle, out var dependencies) ? dependencies : Array.Empty<string>();
    }
    public static class AssetDatabase
    {
        public static string extraDependency;
        public static readonly Dictionary<string, UnityEngine.Object> imported = new(StringComparer.Ordinal);
        public static readonly Dictionary<string, string[]> dependencies = new(StringComparer.Ordinal);
        public static T LoadAssetAtPath<T>(string path) where T : UnityEngine.Object =>
            File.Exists(path) && imported.TryGetValue(path, out var value) ? value as T : null;
        public static string AssetPathToGUID(string path) => File.Exists(path) ? "audited-imported-asset" : "";
        public static string[] GetDependencies(string[] assets, bool recursive) => assets.Concat(new[] { "Resources/unity_builtin_extra" })
            .Concat(assets.SelectMany(asset => dependencies.TryGetValue(asset, out var values) ? values : Array.Empty<string>()))
            .Concat(extraDependency == null ? Array.Empty<string>() : new[] { extraDependency }).Distinct(StringComparer.Ordinal).ToArray();
    }
    public static class BuildPipeline
    {
        public static Action duringBuild;
        public static bool fail;
        public static BuildTarget lastTarget;
        public static BuildAssetBundleOptions lastOptions;
        public static AssetBundleBuild[] lastBuilds;
        public static string[] manifestOverride;
        public static readonly Dictionary<string, string[]> bundleDependencies = new(StringComparer.Ordinal);
        public static readonly Dictionary<string, byte[]> bundleBytes = new(StringComparer.Ordinal)
        {
            ["gloomhavenvr.bundle"] = new byte[] { 1, 2, 3, 4 },
            ["ghvr-town.bundle"] = new byte[] { 5, 6, 7 },
            ["ghvr-town-voices.bundle"] = new byte[] { 8, 9 }
        };
        public static AssetBundleManifest BuildAssetBundles(string output, AssetBundleBuild[] builds, BuildAssetBundleOptions options, BuildTarget target)
        {
            lastTarget = target; lastOptions = options; lastBuilds = builds;
            foreach (var build in builds) File.WriteAllBytes(Path.Combine(output, build.assetBundleName), bundleBytes[build.assetBundleName]);
            duringBuild?.Invoke(); return fail ? null : new AssetBundleManifest();
        }
    }
}
