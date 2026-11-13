using System.Collections;
using GloomhavenVR.Quest;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

internal static class Program
{
    static int assertions;
    static void Check(bool condition, string token)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(token);
    }
    static QuestGameAddressablesManifest Manifest() => new()
    {
        schema = 1, inputKey = "fixture", runtimeSettingsPath = "StreamingAssets/aa/settings.json",
        requiredLabels = new[] { "base", "standalone", "high" },
        aliases = new[] { new QuestGameAddressableAlias { key = "original/key", assetGuid = "native-guid" } }
    };
    static void Run(QuestGameAddressables owner, QuestGameAddressablesManifest manifest)
    {
        IEnumerator operation = owner.Install(manifest, new QuestGameContentManifest(), "/owned", "fixture");
        int iterations = 0;
        while (operation.MoveNext()) Check(++iterations < 20, "bounded-catalog-startup");
        Check(Addressables.AssetLoads == 0, "original-asset-load-owner");
    }
    static void Main()
    {
        Addressables.Reset();
        Func<IResourceLocation, string> original = row => row.InternalId;
        Addressables.InternalIdTransformFunc = original;
        using (var owner = new QuestGameAddressables())
        {
            Run(owner, Manifest());
            Check(owner.Ready && owner.Failure == null, "valid-native-catalog");
            Check(Addressables.Initializations == 1 && Addressables.Locations.SequenceEqual(new[] { "base", "standalone", "high" }), "one-catalog-required-locations");
            Check(Addressables.ResourceLocators.Last().Locate("original/key", typeof(UnityEngine.Object), out var aliases) && aliases.Count == 1, "original-alias-retained");
            Check(Addressables.InternalIdTransformFunc(new Location("jar:stream/aa/real.bundle")) == "/owned/StreamingAssets/aa/real.bundle", "owned-file-transform");
            Check(Addressables.InternalIdTransformFunc(new Location("other:unchanged")) == "other:unchanged", "foreign-id-unchanged");
            Check(Addressables.Releases == 3, "location-handles-released");
        }
        Check(ReferenceEquals(original, Addressables.InternalIdTransformFunc) && Addressables.Releases == 4 && Addressables.ResourceLocators.Count == 1, "catalog-disposal");
        foreach (string defect in new[] { "schema", "input", "duplicate", "empty", "initialization", "locations", "alias" })
        {
            Addressables.Reset(); var manifest = Manifest();
            if (defect == "schema") manifest.schema = 2;
            if (defect == "input") manifest.inputKey = "other";
            if (defect == "duplicate") manifest.requiredLabels = new[] { "base", "base" };
            if (defect == "empty") manifest.requiredLabels = new[] { "" };
            Addressables.Defect = defect;
            using var owner = new QuestGameAddressables(); Run(owner, manifest);
            Check(!owner.Ready && owner.Failure != null, "invalid-catalog-" + defect);
        }
        Console.WriteLine("PASS Quest catalog: " + assertions + " assertions; original asset-load ownership preserved.");
    }
}

internal sealed class Location(string id) : IResourceLocation { public string InternalId => id; public Type ResourceType => typeof(UnityEngine.Object); }
internal sealed class Locator : IResourceLocator
{
    public string LocatorId => "fixture";
    public IEnumerable<object> Keys => new object[] { "native-guid" };
    public bool Locate(object key, Type type, out IList<IResourceLocation> locations)
    {
        locations = new[] { new Location("native") };
        return Addressables.Defect != "alias" && (string)key == "native-guid";
    }
}
namespace GloomhavenVR.Quest
{
    public sealed class QuestGameContentManifest { }
    public static class QuestGameContent
    {
        public static string ResolveVerifiedPath(QuestGameContentManifest content, string root, string relative) => root + "/" + relative;
    }
}
namespace UnityEngine
{
    public class Object { }
    public static class Application { public static string streamingAssetsPath => "jar:stream"; }
    public static class Debug { public static void Log(string value) { } }
    public static class PlayerPrefs { public static void SetString(string key, string value) { } }
}
namespace UnityEngine.ResourceManagement.ResourceLocations
{
    public interface IResourceLocation { string InternalId { get; } Type ResourceType { get; } }
}
namespace UnityEngine.AddressableAssets.ResourceLocators
{
    public interface IResourceLocator { string LocatorId { get; } IEnumerable<object> Keys { get; } bool Locate(object key, Type type, out IList<IResourceLocation> locations); }
}
namespace UnityEngine.ResourceManagement.AsyncOperations
{
    public enum AsyncOperationStatus { None, Succeeded, Failed }
    public struct AsyncOperationHandle { public bool IsValid() => true; }
    public struct AsyncOperationHandle<T>
    {
        public T Result; public AsyncOperationStatus Status; public Exception OperationException;
        public static implicit operator AsyncOperationHandle(AsyncOperationHandle<T> value) => new();
    }
}
namespace UnityEngine.AddressableAssets
{
    public static class Addressables
    {
        public const string kAddressablesRuntimeDataPath = "settings";
        public static string Defect;
        public static int Initializations, AssetLoads, Releases;
        public static List<string> Locations = new();
        public static List<IResourceLocator> ResourceLocators = new();
        public static Func<IResourceLocation, string> InternalIdTransformFunc;
        public static void Reset()
        {
            Defect = null; Initializations = AssetLoads = Releases = 0; Locations.Clear();
            ResourceLocators.Clear(); ResourceLocators.Add(new Locator()); InternalIdTransformFunc = null;
        }
        public static AsyncOperationHandle<IResourceLocator> InitializeAsync(bool release)
        {
            Initializations++;
            return new() { Result = new Locator(), Status = Defect == "initialization" ? AsyncOperationStatus.Failed : AsyncOperationStatus.Succeeded };
        }
        public static AsyncOperationHandle<IList<IResourceLocation>> LoadResourceLocationsAsync(string label, Type type)
        {
            Locations.Add(label);
            return new() { Result = Defect == "locations" ? Array.Empty<IResourceLocation>() : new[] { new Location(label) }, Status = AsyncOperationStatus.Succeeded };
        }
        public static AsyncOperationHandle<IList<T>> LoadAssetsAsync<T>(string label, Action<T> callback) { AssetLoads++; return new(); }
        public static void AddResourceLocator(IResourceLocator locator) => ResourceLocators.Add(locator);
        public static void RemoveResourceLocator(IResourceLocator locator) => ResourceLocators.Remove(locator);
        public static void Release(AsyncOperationHandle handle) => Releases++;
    }
}
