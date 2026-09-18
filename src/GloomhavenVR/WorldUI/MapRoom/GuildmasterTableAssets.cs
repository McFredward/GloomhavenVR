using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>
/// Read-only discovery of the ORIGINAL campaign table, including a cold Guildmaster start.
/// Loads catalog assets asynchronously; never loads/activates a gameplay scene or instantiates
/// a prefab with controllers. All owned handles remain alive until the presentation is released.
/// </summary>
internal sealed class GuildmasterTableAssets
{
    private readonly List<AsyncOperationHandle<UnityEngine.Object>> _loads = new();
    private readonly List<AssetBundleRequest> _bundleLoads = new();
    private readonly List<AsyncOperationHandle<Material>> _materialLoads = new();
    private MaterialLoader[] _nativeLoaders = Array.Empty<MaterialLoader>();
    private MeshRenderer? _materialSource;
    private bool _requiresExactMaterials;
    private string _materialRefusal = "";
    private bool _requested;
    internal Mesh? Mesh;
    internal Material[]? Materials;
    internal Quaternion Rotation = Quaternion.identity;
    internal Vector3 Scale = Vector3.one;
    internal int Layer;
    internal string Diagnostic => $"mesh={(Mesh != null ? Mesh.name : "none")}, materials={Materials?.Length ?? 0}, "
        + $"catalogRequests={_loads.Count}, bundleRequests={_bundleLoads.Count}, exactMaterialRequests={_materialLoads.Count}, "
        + $"materialRefusal={_materialRefusal}";

    private static bool TableName(string name) => name.IndexOf("GH_Map_TableTop_Lg", StringComparison.OrdinalIgnoreCase) >= 0;
    private static bool TableMaterial(string name) => name.IndexOf("GH_Map_Table", StringComparison.OrdinalIgnoreCase) >= 0;

    internal bool Poll()
    {
        if (!_requested)
        {
            _requested = true;
            // The assets can already be resident without a campaign ever having been entered:
            // native shared bundles and inactive scene objects are both included in this sweep.
            _nativeLoaders = Resources.FindObjectsOfTypeAll<MaterialLoader>();
            MeshRenderer[] renderers = Resources.FindObjectsOfTypeAll<MeshRenderer>();
            for (int i = 0; i < renderers.Length; i++)
                ReadRenderer(renderers[i]);
            if (Mesh == null)
            {
                Mesh[] meshes = Resources.FindObjectsOfTypeAll<Mesh>();
                for (int i = 0; i < meshes.Length; i++)
                    if (TableName(meshes[i].name)) ReadMesh(meshes[i]);
            }
            if (Materials == null && !_requiresExactMaterials)
            {
                Material[] materials = Resources.FindObjectsOfTypeAll<Material>();
                for (int i = 0; i < materials.Length; i++)
                    if (TableMaterial(materials[i].name) && materials[i].mainTexture != null)
                    { Materials = new[] { materials[i] }; break; }
            }
            if (Mesh == null || (Materials == null && !_requiresExactMaterials))
            {
                RequestCatalogAssets();
                RequestLoadedBundleAssets();
            }
        }
        for (int i = 0; i < _loads.Count; i++)
        {
            AsyncOperationHandle<UnityEngine.Object> load = _loads[i];
            if (!load.IsDone || load.Status != AsyncOperationStatus.Succeeded) continue;
            ReadAsset(load.Result);
        }
        for (int i = 0; i < _bundleLoads.Count; i++)
        {
            AssetBundleRequest load = _bundleLoads[i];
            if (!load.isDone) continue;
            UnityEngine.Object[] assets = load.allAssets;
            for (int j = 0; j < assets.Length; j++) ReadAsset(assets[j]);
        }
        ResolveExactMaterials();
        return Mesh != null && Materials != null && Materials.Length == Mesh.subMeshCount;
    }

    internal bool Pending
    {
        get
        {
            for (int i = 0; i < _loads.Count; i++) if (!_loads[i].IsDone) return true;
            for (int i = 0; i < _bundleLoads.Count; i++) if (!_bundleLoads[i].isDone) return true;
            for (int i = 0; i < _materialLoads.Count; i++) if (!_materialLoads[i].IsDone) return true;
            return false;
        }
    }

    private void ReadAsset(UnityEngine.Object asset)
    {
        if (asset is GameObject prefab)
        {
            MeshRenderer[] renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
            for (int j = 0; j < renderers.Length; j++) ReadRenderer(renderers[j]);
        }
        else if (asset is Mesh mesh && Mesh == null && TableName(mesh.name)) ReadMesh(mesh);
        else if (asset is Material material && Materials == null && !_requiresExactMaterials && TableMaterial(material.name)) Materials = new[] { material };
    }

    private void ReadRenderer(MeshRenderer renderer)
    {
        if (!TableName(renderer.name) || _materialSource != null) return;
        MeshFilter? filter = renderer.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null) return;
        Mesh = filter.sharedMesh;
        Rotation = renderer.transform.rotation;
        Scale = renderer.transform.lossyScale;
        Layer = renderer.gameObject.layer;
        _materialSource = renderer;
        Material[] materials = renderer.sharedMaterials;
        MaterialLoaderData? data = FindMaterialData(renderer);
        if (data != null && data.MaterialReferences != null && data.MaterialReferences.Count > 0)
        {
            // MaterialLoaderData.LoadMaterials disables the renderer but leaves its old non-null
            // slots in place until ALL references complete. A prefab has not run Start at all.
            // Neither enabled nor non-null slots prove readiness. Read the exact ordered native
            // references, acquire independent handles, and never invoke/mutate the native loader.
            _requiresExactMaterials = true;
            Materials = null;
            if (data.IsSaveExistedMaterials)
            {
                // That native branch reserves extra slots, waits for ALL slots to be non-null,
                // then overwrites reference indices with existing slots. Without an authoritative
                // completed array there is no justified reconstructed order. Do not guess one.
                for (int i = 0; i < materials.Length; i++)
                    if (materials[i] != null)
                    { _materialRefusal = "native retained-material layout has no verified final slot order"; return; }
            }
            for (int i = 0; i < data.MaterialReferences.Count; i++)
            {
                var reference = data.MaterialReferences[i];
                if (reference == null || !reference.RuntimeKeyIsValid())
                { _materialRefusal = $"native material reference {i} is invalid"; return; }
            }
            for (int i = 0; i < data.MaterialReferences.Count; i++)
                _materialLoads.Add(Addressables.LoadAssetAsync<Material>(data.MaterialReferences[i].RuntimeKey));
            return;
        }
        if (materials.Length != filter.sharedMesh.subMeshCount) return;
        for (int i = 0; i < materials.Length; i++) if (materials[i] == null) return;
        Materials = materials;
    }

    private MaterialLoaderData? FindMaterialData(MeshRenderer renderer)
    {
        // Resident loader records can point to a renderer outside their own child hierarchy.
        // Newly loaded prefabs are covered by the ancestor scan without running their Start.
        MaterialLoaderData? data = FindMaterialData(_nativeLoaders, renderer);
        return data ?? FindMaterialData(renderer.GetComponentsInParent<MaterialLoader>(true), renderer);
    }

    private static MaterialLoaderData? FindMaterialData(MaterialLoader[] loaders, MeshRenderer renderer)
    {
        for (int i = 0; i < loaders.Length; i++)
        {
            if (loaders[i] == null || loaders[i].LoadersData == null) continue;
            for (int j = 0; j < loaders[i].LoadersData.Count; j++)
            {
                MaterialLoaderData data = loaders[i].LoadersData[j];
                if (data != null && data.Renderer == renderer) return data;
            }
        }
        return null;
    }

    private void ResolveExactMaterials()
    {
        if (!_requiresExactMaterials || Materials != null || _materialRefusal.Length != 0 || _materialLoads.Count == 0) return;
        for (int i = 0; i < _materialLoads.Count; i++)
        {
            AsyncOperationHandle<Material> load = _materialLoads[i];
            if (!load.IsDone) return;
            if (load.Status != AsyncOperationStatus.Succeeded || load.Result == null)
            { _materialRefusal = $"native material reference {i} failed"; return; }
        }
        var materials = new Material[_materialLoads.Count];
        for (int i = 0; i < materials.Length; i++) materials[i] = _materialLoads[i].Result;
        Materials = materials;
    }

    private void ReadMesh(Mesh mesh)
    {
        Mesh = mesh;
        // Standalone meshes have no transform. A tabletop's thin local axis identifies its up.
        Vector3 size = mesh.bounds.size;
        Rotation = size.y <= size.x && size.y <= size.z ? Quaternion.identity
            : size.z <= size.x ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.Euler(0f, 0f, 90f);
        Scale = Vector3.one;
    }

    private void RequestCatalogAssets()
    {
        var requested = new HashSet<string>(StringComparer.Ordinal);
        foreach (var locator in Addressables.ResourceLocators)
        {
            foreach (object key in locator.Keys)
            {
                if (!(key is string name)) continue;
                bool prefabOrMesh = TableName(name);
                bool material = TableMaterial(name) && name.EndsWith(".mat", StringComparison.OrdinalIgnoreCase);
                if (!prefabOrMesh && !material) continue;
                if (!locator.Locate(key, typeof(UnityEngine.Object), out IList<IResourceLocation> locations)) continue;
                for (int i = 0; i < locations.Count; i++)
                {
                    IResourceLocation location = locations[i];
                    Type type = location.ResourceType;
                    // Catalog scene keys must never become scene loads through an object request.
                    if (type != typeof(GameObject) && type != typeof(Mesh) && type != typeof(Material)) continue;
                    if (requested.Add(location.InternalId))
                        _loads.Add(Addressables.LoadAssetAsync<UnityEngine.Object>(location));
                }
            }
        }
    }

    private void RequestLoadedBundleAssets()
    {
        // Addressables can expose only a containing FBX/prefab, not its mesh subasset. Reading
        // matching assets from already resident native bundles covers that case without a scene
        // load, bundle replacement, a synchronous WaitForCompletion, or native handle ownership.
        foreach (AssetBundle bundle in AssetBundle.GetAllLoadedAssetBundles())
        {
            if (bundle.isStreamedSceneAssetBundle) continue;
            string[] names = bundle.GetAllAssetNames();
            for (int i = 0; i < names.Length; i++)
                if (TableName(names[i]) || (TableMaterial(names[i]) && names[i].EndsWith(".mat", StringComparison.OrdinalIgnoreCase)))
                    _bundleLoads.Add(bundle.LoadAssetWithSubAssetsAsync<UnityEngine.Object>(names[i]));
        }
    }

    internal void Release()
    {
        for (int i = 0; i < _loads.Count; i++)
            if (_loads[i].IsValid()) Addressables.Release(_loads[i]);
        for (int i = 0; i < _materialLoads.Count; i++)
            if (_materialLoads[i].IsValid()) Addressables.Release(_materialLoads[i]);
        _materialLoads.Clear();
        _nativeLoaders = Array.Empty<MaterialLoader>();
        _materialSource = null;
        _requiresExactMaterials = false;
        _materialRefusal = "";
        _loads.Clear();
        _bundleLoads.Clear();
        Mesh = null;
        Materials = null;
        _requested = false;
        Rotation = Quaternion.identity;
        Scale = Vector3.one;
        Layer = 0;
    }
}
