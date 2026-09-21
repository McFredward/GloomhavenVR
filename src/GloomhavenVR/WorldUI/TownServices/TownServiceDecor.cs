using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace GloomhavenVR.WorldUI;

/// <summary>Original game decoration, copied as rendering data without any native controllers.</summary>
internal sealed class TownServiceDecor : IDisposable
{
    private sealed class Piece
    {
        internal string Key = string.Empty, Entry = string.Empty;
        internal Vector3 Position;
        internal float Size;
        internal bool Candle;
        internal int LightSlot;
        internal AsyncOperationHandle<ApparanceResourceList> Handle;
        internal GameObject? Source;
        internal bool Inspected, Built;
    }
    private readonly List<Piece> _pieces = new();
    private readonly Dictionary<object, AsyncOperationHandle<Material>> _loads = new();
    private readonly List<Material> _materials = new();
    private readonly Transform _root;
    private readonly TownServiceLighting _lighting;
    private float _visibility;
    private float _clock;
    private bool _reported;
    private static readonly int Visibility = Shader.PropertyToID("_TownVisibility");

    internal TownServiceDecor(Transform station, byte service, TownServiceLighting lighting)
    {
        _root = station;
        _lighting = lighting;
        try { Populate(service); }
        catch { Dispose(); throw; }
    }

    private void Populate(byte service)
    {
        if (service == 1)
            Add("Gaslight", "Gaslight.Lighting.Torch.Wall#1", new Vector3(-.61f, .957f, .17f), .42f, true);
        else
            Add("Tone_Candlelight", service == 2 ? "Candlelight.Lighting.Torch.Wall#3" : "Candlelight.Lighting.Torch.Wall#1",
                new Vector3(-.61f, .957f, .17f), .38f, true);
        if (service == 1)
        {
            Add("Treasure", "Treasure.Clutter.FloorSmall#3", new Vector3(.59f, .957f, .12f), .20f);
            Add("Library", "Library.Clutter.Shelf.Individual#3", new Vector3(-.53f, .957f, -.14f), .21f);
        }
        else if (service == 2)
        {
            Add("Tone_Candlelight", "Candlelight.Lighting.Torch.Wall#3", new Vector3(.61f, .957f, .17f), .32f, true, 1);
            Add("Chapel", "Chapel.Clutter.Shelf.Individual#7", new Vector3(.55f, .957f, -.13f), .15f);
        }
        else
        {
            Add("AlchemyLab", "AlchemyLab.Clutter.Shelf.Individual#3", new Vector3(.59f, .957f, .15f), .22f);
            Add("Library", "Library.Clutter.Shelf.Individual#1", new Vector3(.53f, .957f, -.13f), .20f);
        }
    }

    private void Add(string list, string entry, Vector3 position, float size, bool candle = false, int lightSlot = 0)
    {
        string key = "Assets/PCG/PCG_" + list + ".asset";
        _pieces.Add(new Piece { Key = key, Entry = entry, Position = position, Size = size, Candle = candle, LightSlot = lightSlot,
            Handle = Addressables.LoadAssetAsync<ApparanceResourceList>(key) });
    }

    internal void Tick()
    {
        foreach (Piece piece in _pieces)
        {
            if (piece.Built || !piece.Handle.IsDone) continue;
            try
            {
                if (!piece.Inspected)
                {
                    piece.Inspected = true;
                    if (piece.Handle.Status != AsyncOperationStatus.Succeeded) throw new InvalidOperationException("Original decoration list unavailable: " + piece.Key);
                    foreach (ApparanceObjectResource resource in piece.Handle.Result.Objects)
                        if (resource.Name == piece.Entry) { piece.Source = resource.Object as GameObject; break; }
                    if (piece.Source == null) throw new InvalidOperationException("Original decoration missing: " + piece.Entry);
                    foreach (MaterialLoader loader in piece.Source.GetComponentsInChildren<MaterialLoader>(true))
                        foreach (MaterialLoaderData data in loader.LoadersData)
                            foreach (AssetReferenceT<Material> reference in data.MaterialReferences)
                                if (reference != null && reference.RuntimeKeyIsValid() && !_loads.ContainsKey(reference.RuntimeKey))
                                    _loads.Add(reference.RuntimeKey, Addressables.LoadAssetAsync<Material>(reference.RuntimeKey));
                }
                bool ready = true;
                foreach (AsyncOperationHandle<Material> load in _loads.Values)
                {
                    if (!load.IsDone) ready = false;
                    else if (load.Status != AsyncOperationStatus.Succeeded) throw new InvalidOperationException("Original decoration material unavailable");
                }
                if (!ready) continue;
                Build(piece);
                piece.Built = true;
            }
            catch (Exception e)
            {
                piece.Built = true;
                if (!_reported)
                {
                    _reported = true;
                    VRLog.Warn("TownServices", "Original town decoration could not be prepared: " + e.Message);
                }
            }
        }
    }

    private void Build(Piece piece)
    {
        var holder = new GameObject("Original." + piece.Entry);
        holder.SetActive(false);
        try
        {
            holder.transform.SetParent(_root, false);
            holder.transform.localPosition = piece.Position;
            var overrides = new Dictionary<Renderer, Material[]>();
            foreach (MaterialLoader loader in piece.Source!.GetComponentsInChildren<MaterialLoader>(true))
                foreach (MaterialLoaderData data in loader.LoadersData)
                {
                    if (data.Renderer == null) continue;
                    var materials = new List<Material>();
                    foreach (AssetReferenceT<Material> reference in data.MaterialReferences)
                        if (reference != null && _loads.TryGetValue(reference.RuntimeKey, out AsyncOperationHandle<Material> load)) materials.Add(load.Result);
                    if (data.IsSaveExistedMaterials)
                        foreach (Material material in data.Renderer.sharedMaterials) if (material != null) materials.Add(material);
                    overrides[data.Renderer] = materials.ToArray();
                }
            Copy(piece.Source.transform, holder.transform, overrides);
            Bounds bounds = default;
            bool any = false;
            foreach (MeshFilter filter in holder.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || IsFlame(filter.name)) continue;
                Bounds local = filter.sharedMesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = local.center + Vector3.Scale(local.extents,
                        new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    Vector3 p = holder.transform.InverseTransformPoint(filter.transform.TransformPoint(corner));
                    if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; } else bounds.Encapsulate(p);
                }
            }
            if (!any || bounds.size.sqrMagnitude < 1e-8f) { UnityEngine.Object.Destroy(holder); return; }
            float size = piece.Candle ? bounds.size.y : Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            float factor = piece.Size / Mathf.Max(.0001f, size);
            holder.transform.localScale = Vector3.one * factor;
            holder.transform.localPosition = piece.Position - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) * factor;
            if (piece.Candle)
            {
                Transform? flame = null;
                foreach (Transform child in holder.GetComponentsInChildren<Transform>(true))
                    if (child.name.IndexOf("Flame", StringComparison.OrdinalIgnoreCase) >= 0 || child.name == "Point Light") { flame = child; break; }
                _lighting.SetFlame(flame != null ? flame.position : _root.TransformPoint(piece.Position + Vector3.up * piece.Size), piece.LightSlot);
            }
            holder.SetActive(true);
        }
        catch { UnityEngine.Object.Destroy(holder); throw; }
    }

    private void Copy(Transform source, Transform parent, Dictionary<Renderer, Material[]> overrides)
    {
        // Reconstruct, do not Instantiate: even disabled MonoBehaviours can execute Awake.
        var obj = new GameObject(source.name) { layer = VRLayers.ModLayer };
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = source.localPosition;
        obj.transform.localRotation = source.localRotation;
        obj.transform.localScale = source.localScale;
        MeshFilter? filter = source.GetComponent<MeshFilter>();
        MeshRenderer? renderer = source.GetComponent<MeshRenderer>();
        if (filter != null && filter.sharedMesh != null && renderer != null)
        {
            obj.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            MeshRenderer copy = obj.AddComponent<MeshRenderer>();
            Material[] originals = overrides.TryGetValue(renderer, out Material[] found) ? found : renderer.sharedMaterials;
            var materials = new Material[originals.Length];
            bool flame = IsFlame(source.name);
            bool glass = source.name.EndsWith("_Glass", StringComparison.Ordinal);
            for (int i = 0; i < originals.Length; i++) materials[i] = Adapt(originals[i], flame || glass, flame);
            copy.sharedMaterials = materials;
            copy.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            copy.receiveShadows = false;
        }
        foreach (Transform child in source) Copy(child, obj.transform, overrides);
    }

    private static bool IsFlame(string name) => name.IndexOf("Flame", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("Glow", StringComparison.OrdinalIgnoreCase) >= 0;

    private Material Adapt(Material source, bool flame, bool billboard)
    {
        Shader? shader = TownServiceAssets.Shader(flame ? "townflame" : "townnpc");
        if (source == null || shader == null) throw new InvalidOperationException("Original decoration material or town shader missing");
        // Preserve original texture data, UV transform, tint and normal map. Change only the
        // render contract (stereo, practical lighting and station dissolve), on an owned copy.
        var material = new Material(source) { name = "Town.Original." + source.name, shader = shader, renderQueue = shader.renderQueue };
        material.SetFloat(Visibility, _visibility);
        if (flame)
        {
            if (source.HasProperty("_TintColor")) material.SetColor("_Color", source.GetColor("_TintColor"));
            if (material.HasProperty("_Billboard")) material.SetFloat("_Billboard", billboard ? 1f : 0f);
            if (material.HasProperty("_TownAnimationTime")) material.SetFloat("_TownAnimationTime", _clock);
        }
        if (source.HasProperty("_BumpMap") && source.GetTexture("_BumpMap") != null) material.EnableKeyword("_NORMALMAP");
        _materials.Add(material);
        return material;
    }

    internal void SetClock(float seconds)
    {
        _clock = seconds;
        foreach (Material material in _materials)
            if (material.HasProperty("_TownAnimationTime")) material.SetFloat("_TownAnimationTime", seconds);
    }

    internal void SetVisibility(float value)
    {
        _visibility = value;
        foreach (Material material in _materials) material.SetFloat(Visibility, value);
    }

    public void Dispose()
    {
        foreach (Material material in _materials) UnityEngine.Object.Destroy(material);
        foreach (AsyncOperationHandle<Material> load in _loads.Values) if (load.IsValid()) Addressables.Release(load);
        foreach (Piece piece in _pieces) if (piece.Handle.IsValid()) Addressables.Release(piece.Handle);
        _materials.Clear(); _loads.Clear(); _pieces.Clear();
    }
}
