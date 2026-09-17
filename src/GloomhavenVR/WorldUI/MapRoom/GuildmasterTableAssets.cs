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
    private bool _requested;
    internal Mesh? Mesh;
    internal Material[]? Materials;
    internal Quaternion Rotation = Quaternion.identity;
    internal Vector3 Scale = Vector3.one;
    internal int Layer;
    internal string Diagnostic => $"mesh={(Mesh != null ? Mesh.name : "none")}, materials={Materials?.Length ?? 0}, "
        + $"catalogRequests={_loads.Count}, bundleRequests={_bundleLoads.Count}";

    private static bool TableName(string name) => name.IndexOf("GH_Map_TableTop_Lg", StringComparison.OrdinalIgnoreCase) >= 0;
    private static bool TableMaterial(string name) => name.IndexOf("GH_Map_Table", StringComparison.OrdinalIgnoreCase) >= 0;

    internal bool Poll()
    {
        if (!_requested)
        {
            _requested = true;
            // The assets can already be resident without a campaign ever having been entered:
            // native shared bundles and inactive scene objects are both included in this sweep.
            MeshRenderer[] renderers = Resources.FindObjectsOfTypeAll<MeshRenderer>();
            for (int i = 0; i < renderers.Length; i++)
                ReadRenderer(renderers[i]);
            if (Mesh == null)
            {
                Mesh[] meshes = Resources.FindObjectsOfTypeAll<Mesh>();
                for (int i = 0; i < meshes.Length; i++)
                    if (TableName(meshes[i].name)) ReadMesh(meshes[i]);
            }
            if (Materials == null)
            {
                Material[] materials = Resources.FindObjectsOfTypeAll<Material>();
                for (int i = 0; i < materials.Length; i++)
                    if (TableMaterial(materials[i].name) && materials[i].mainTexture != null)
                    { Materials = new[] { materials[i] }; break; }
            }
            if (Mesh == null || Materials == null)
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
        return Mesh != null && Materials != null && Materials.Length == Mesh.subMeshCount;
    }

    internal bool Pending
    {
        get
        {
            for (int i = 0; i < _loads.Count; i++) if (!_loads[i].IsDone) return true;
            for (int i = 0; i < _bundleLoads.Count; i++) if (!_bundleLoads[i].isDone) return true;
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
        else if (asset is Material material && Materials == null && TableMaterial(material.name)) Materials = new[] { material };
    }

    private void ReadRenderer(MeshRenderer renderer)
    {
        if (!TableName(renderer.name)) return;
        MeshFilter? filter = renderer.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null) return;
        Mesh = filter.sharedMesh;
        Rotation = renderer.transform.rotation;
        Scale = renderer.transform.lossyScale;
        Layer = renderer.gameObject.layer;
        // A prefab may carry a MaterialLoader and empty slots until instantiated. Its mesh is
        // still usable; obtain its original material independently instead of running that loader.
        Material[] materials = renderer.sharedMaterials;
        if (materials.Length != filter.sharedMesh.subMeshCount) return;
        for (int i = 0; i < materials.Length; i++) if (materials[i] == null) return;
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
