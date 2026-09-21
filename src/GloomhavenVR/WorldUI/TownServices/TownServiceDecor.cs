using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Net;
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
        internal bool Candle, Arcane;
        internal Material[] EffectMaterials = Array.Empty<Material>();
        internal GameObject? Holder;
        internal Vector3 Home;
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
    private readonly TownServiceActivityProps _work;
    private Piece? _workCoin, _arcane;
    private Transform? _castGrip;
    private bool _castGripBound;
    private float _visibility;
    private float _clock;
    private bool _reported;
    private static readonly int Visibility = Shader.PropertyToID("_TownVisibility");

    internal TownServiceDecor(Transform station, byte service, TownServiceLighting lighting)
    {
        _root = station;
        _lighting = lighting;
        _work = new TownServiceActivityProps(station, service, TownServiceAssets.Shader("townnpc"));
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
            Add("Library", "Library.Clutter.Shelf.Individual#7", new Vector3(0f, .957f, .22f), .32f);
            _workCoin = Add("Treasure", "Treasure.Clutter.Shelf.Individual#1", new Vector3(-.20f, .96f, .22f), .026f);
        }
        else if (service == 2)
        {
            Add("Tone_Candlelight", "Candlelight.Lighting.Torch.Wall#3", new Vector3(.61f, .957f, .17f), .32f, true, 1);
            Add("Chapel", "Chapel.Clutter.Shelf.Individual#7", new Vector3(.55f, .957f, -.13f), .15f);
        }
        else
        {
            Add("AlchemyLab", "AlchemyLab.Clutter.Shelf.Individual#3", new Vector3(.59f, .957f, .15f), .22f);
            Add("Library", "Library.Clutter.Shelf.Individual#7", new Vector3(0f, .957f, .22f), .32f);
            _arcane = Add("Tone_Candlelight", "Candlelight.Lighting.Torch.Wall#1", Vector3.zero, .075f, arcane: true);
        }
    }

    private Piece Add(string list, string entry, Vector3 position, float size, bool candle = false, int lightSlot = 0, bool arcane = false)
    {
        string key = "Assets/PCG/PCG_" + list + ".asset";
        var piece = new Piece { Key = key, Entry = entry, Position = position, Size = size, Candle = candle, LightSlot = lightSlot, Arcane = arcane,
            Handle = Addressables.LoadAssetAsync<ApparanceResourceList>(key) };
        _pieces.Add(piece);
        return piece;
    }

    internal bool Ready
    {
        get
        {
            foreach (Piece piece in _pieces) if (!piece.Built) return false;
            return true;
        }
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
            Transform visual = piece.Source.transform;
            if (piece.Arcane)
            {
                // Reuse the original candle's soft glow texture/quad as a restrained hand
                // effect. Copy rendering data only; never run native particle controllers.
                Transform? glow = null;
                foreach (Transform child in visual.GetComponentsInChildren<Transform>(true))
                    if (child.name.IndexOf("Glow", StringComparison.OrdinalIgnoreCase) >= 0
                        && child.GetComponent<MeshFilter>() != null) { glow = child; break; }
                if (glow == null) throw new InvalidOperationException("Original candle glow mesh unavailable");
                visual = glow;
            }
            int materialStart = _materials.Count;
            Copy(visual, holder.transform, overrides);
            if (piece.Arcane)
            {
                piece.EffectMaterials = _materials.GetRange(materialStart, _materials.Count - materialStart).ToArray();
                foreach (Material material in piece.EffectMaterials)
                {
                    material.SetColor("_Color", new Color(.30f, .65f, .90f, .55f));
                    material.SetFloat(Visibility, 0f);
                }
            }
            Bounds bounds = default;
            bool any = false;
            foreach (MeshFilter filter in holder.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || (!piece.Arcane && IsFlame(filter.name))) continue;
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
            piece.Holder = holder;
            piece.Home = holder.transform.localPosition;
            if (piece == _workCoin) _work.BindCoin(holder.transform, piece.Home - piece.Position);
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

    internal void SampleActivity(in TownActivityPose pose)
    {
        _work.Sample(in pose);
        if (_arcane?.Holder == null) return;
        if (!_castGripBound)
        {
            // The station constructs its immutable activity rig before decoration. Missing
            // grips in an incompatible bundle must not turn into a hierarchy scan per frame.
            _castGripBound = true;
            foreach (Transform child in _root.GetComponentsInChildren<Transform>(true))
                if (child.name == "ActivityGripRight") { _castGrip = child; break; }
        }
        if (_castGrip == null) return;
        float strength = TownServiceActivityMotion.Pulse(pose.WorkClock % 14f, 7f, 12f)
            * (1f - TownServiceActivityMotion.Blend(in pose));
        Transform effect = _arcane.Holder.transform;
        effect.localPosition = _arcane.Home + _root.InverseTransformPoint(_castGrip.position)
            + new Vector3(.018f * Mathf.Sin(pose.WorkClock * 2f), .035f, -.02f);
        foreach (Material material in _arcane.EffectMaterials)
            material.SetFloat(Visibility, _visibility * strength);
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
        _work.SetVisibility(value);
        foreach (Material material in _materials) material.SetFloat(Visibility, value);
        // Only a successfully sampled activity may expose this optional effect. An absent
        // grip or a disabled cosmetic rig must not leave a bright orphan at station origin.
        if (_arcane != null)
            foreach (Material material in _arcane.EffectMaterials) material.SetFloat(Visibility, 0f);
    }

    public void Dispose()
    {
        _work.Dispose();
        foreach (Material material in _materials) UnityEngine.Object.Destroy(material);
        foreach (AsyncOperationHandle<Material> load in _loads.Values) if (load.IsValid()) Addressables.Release(load);
        foreach (Piece piece in _pieces) if (piece.Handle.IsValid()) Addressables.Release(piece.Handle);
        _materials.Clear(); _loads.Clear(); _pieces.Clear();
    }
}
