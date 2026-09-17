using System;
using System.Collections.Generic;
using System.Linq;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace UnityEngine
{
    public class Object { public string name = ""; }
    public class Material : Object { public Object? mainTexture = null; }
    public class Mesh : Object { public Bounds bounds = new(); public int subMeshCount = 1; }
    public struct Bounds { public Vector3 size; }
    public struct Vector3 { public float x, y, z; public static Vector3 one => new() { x = 1, y = 1, z = 1 }; }
    public struct Quaternion { public static Quaternion identity => new(); public static Quaternion Euler(float x, float y, float z) => new(); }
    public class Transform { public Quaternion rotation = new(); public Vector3 lossyScale = Vector3.one; }
    public class GameObject : Object
    {
        public int layer = 0;
        public T[] GetComponentsInChildren<T>(bool includeInactive) => Array.Empty<T>();
    }
    public class MeshFilter { public Mesh? sharedMesh = null; }
    public class MeshRenderer : Object
    {
        public Material[] sharedMaterials = Array.Empty<Material>();
        public MeshFilter Filter = new();
        public Transform transform = new();
        public GameObject gameObject = new();
        public MaterialLoader[] Ancestors = Array.Empty<MaterialLoader>();
        public bool enabled = true;
        public T? GetComponent<T>() where T : class => Filter as T;
        public T[] GetComponentsInParent<T>(bool includeInactive) => Ancestors.OfType<T>().ToArray();
    }
    public static class Resources
    {
        public static readonly List<Object> Resident = new();
        public static T[] FindObjectsOfTypeAll<T>() => Resident.OfType<T>().ToArray();
    }
    public class AssetBundle : Object
    {
        public bool isStreamedSceneAssetBundle = false;
        public static AssetBundle[] GetAllLoadedAssetBundles() => Array.Empty<AssetBundle>();
        public string[] GetAllAssetNames() => Array.Empty<string>();
        public AssetBundleRequest LoadAssetWithSubAssetsAsync<T>(string name) => new();
    }
    public class AssetBundleRequest { public bool isDone = true; public Object[] allAssets = Array.Empty<Object>(); }
}
public sealed class MaterialLoader : UnityEngine.Object { public List<MaterialLoaderData> LoadersData = new(); }
public sealed class MaterialLoaderData
{
    public MeshRenderer? Renderer = null;
    public bool IsSaveExistedMaterials = false;
    public List<AssetReferenceT<Material>> MaterialReferences = new();
}
namespace UnityEngine.ResourceManagement.ResourceLocations
{
    public interface IResourceLocation { Type ResourceType { get; } string InternalId { get; } }
}
namespace UnityEngine.ResourceManagement.AsyncOperations
{
    public enum AsyncOperationStatus { None, Succeeded, Failed }
    public sealed class State { public bool Done = false; public AsyncOperationStatus Status; public UnityEngine.Object? Result = null; public int Releases = 0; }
    public struct AsyncOperationHandle<T> where T : UnityEngine.Object
    {
        public State Value;
        public bool IsDone => Value.Done;
        public AsyncOperationStatus Status => Value.Status;
        public T Result => (T)Value.Result!;
        public bool IsValid() => Value != null;
    }
}
namespace UnityEngine.AddressableAssets
{
    public sealed class AssetReferenceT<T> where T : UnityEngine.Object
    {
        public object RuntimeKey = "";
        public bool Valid = true;
        public bool RuntimeKeyIsValid() => Valid;
    }
    public sealed class Locator
    {
        public object[] Keys => Array.Empty<object>();
        public bool Locate(object key, Type type, out IList<UnityEngine.ResourceManagement.ResourceLocations.IResourceLocation> locations)
        { locations = Array.Empty<UnityEngine.ResourceManagement.ResourceLocations.IResourceLocation>(); return false; }
    }
    public static class Addressables
    {
        public static readonly List<object> Requested = new();
        public static readonly List<State> States = new();
        public static Locator[] ResourceLocators => Array.Empty<Locator>();
        public static AsyncOperationHandle<T> LoadAssetAsync<T>(object key) where T : UnityEngine.Object
        {
            Requested.Add(key);
            var state = new State(); States.Add(state);
            return new AsyncOperationHandle<T> { Value = state };
        }
        public static void Release<T>(AsyncOperationHandle<T> handle) where T : UnityEngine.Object => handle.Value.Releases++;
    }
}
internal static class TableAssetVectors
{
    private static int _checks;
    private static void Check(bool condition, string reason)
    { _checks++; if (!condition) throw new Exception(reason); }
    private static (MeshRenderer Renderer, MaterialLoaderData Data, Material Placeholder) Fixture(bool enabled = true)
    {
        Resources.Resident.Clear(); Addressables.Requested.Clear(); Addressables.States.Clear();
        var placeholder = new Material { name = "GH_Map_Table_Placeholder", mainTexture = new UnityEngine.Object() };
        var renderer = new MeshRenderer
        {
            name = "GH_Map_TableTop_Lg", enabled = enabled,
            sharedMaterials = new[] { placeholder, placeholder },
            Filter = new MeshFilter { sharedMesh = new Mesh { name = "GH_Map_TableTop_Lg", subMeshCount = 2 } }
        };
        var data = new MaterialLoaderData { Renderer = renderer, MaterialReferences = new()
        {
            new() { RuntimeKey = "opaque-guid-first" }, new() { RuntimeKey = "opaque-guid-second" }
        } };
        var loader = new MaterialLoader { LoadersData = new() { data } };
        renderer.Ancestors = new[] { loader };
        Resources.Resident.Add(renderer); Resources.Resident.Add(loader); Resources.Resident.Add(placeholder);
        return (renderer, data, placeholder);
    }
    internal static int Run()
    {
        foreach (bool enabled in new[] { true, false })
        {
            var fixture = Fixture(enabled);
            var assets = new GuildmasterTableAssets();
            Check(!assets.Poll(), "placeholder materials are not ready");
            Check(assets.Materials == null && assets.Pending, "exact materials remain pending");
            Check(Addressables.Requested.SequenceEqual(new object[] { "opaque-guid-first", "opaque-guid-second" }), "exact ordered native material keys");
            State first = Addressables.States[0], second = Addressables.States[1];
            var a = new Material { name = "original first" }; var b = new Material { name = "original second" };
            second.Result = b; second.Done = true; second.Status = AsyncOperationStatus.Succeeded;
            Check(!assets.Poll(), "out-of-order completion is not partially published");
            first.Result = a; first.Done = true; first.Status = AsyncOperationStatus.Succeeded;
            Check(assets.Poll(), "all exact materials publish");
            Check(assets.Materials![0] == a && assets.Materials[1] == b, "completion order never changes slot order");
            Check(fixture.Renderer.enabled == enabled && fixture.Renderer.sharedMaterials[0] == fixture.Placeholder, "native renderer untouched");
            Check(Addressables.Requested.Count == 2, "no duplicate material requests during polling");
            assets.Release(); assets.Release();
            Check(first.Releases == 1 && second.Releases == 1, "own material handles released once");
            Check(fixture.Data.MaterialReferences.Count == 2, "native references unchanged");
        }
        Fixture();
        var failed = new GuildmasterTableAssets();
        Check(!failed.Poll(), "failure fixture pending");
        foreach (State state in Addressables.States) { state.Done = true; state.Status = AsyncOperationStatus.Failed; }
        Check(!failed.Poll() && failed.Materials == null && !failed.Pending, "failed load never exposes placeholder");
        failed.Release();
        var invalidFixture = Fixture(); invalidFixture.Data.MaterialReferences[1].Valid = false;
        var invalid = new GuildmasterTableAssets();
        Check(!invalid.Poll() && Addressables.Requested.Count == 0, "invalid native key fails before partial acquisition");
        invalid.Release();
        var retainedFixture = Fixture(); retainedFixture.Data.IsSaveExistedMaterials = true;
        var retained = new GuildmasterTableAssets();
        Check(!retained.Poll() && retained.Materials == null, "unproven native retained-material layout never guessed");
        retained.Release();
        Console.WriteLine($"Guildmaster native materials: {_checks} assertions passed.");
        return _checks;
    }
}
