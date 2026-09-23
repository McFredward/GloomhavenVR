using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Two compact vertical card racks inside a travelling merchant cabinet.
/// Inventory size changes the number of mechanical turns, never the furniture footprint.</summary>
internal static class TownServiceMerchantLayout
{
    internal const int StockColumns = 4, StockRows = 4;
    internal const int StockCapacity = StockColumns * StockRows;
    internal const int ReturnColumns = 4, ReturnRows = 4;
    internal const int ReturnCapacity = ReturnColumns * ReturnRows;
    internal const float CardWidth = .14f, CardHeight = .112f, FacePitch = 0f;
    internal const float WorktopHeight = .970f, ColumnPitch = .15f, RowPitch = .14f, TerraceRise = 0f;
    internal const float CardSeatLift = .034f;
    internal static Vector3 StockPosition(int ordinal) => new Vector3(
        (ordinal % StockColumns - 1.5f) * ColumnPitch,
        -.245f + (ordinal / StockColumns % StockRows) * RowPitch, -.025f);
    internal static Vector3 ReturnPosition(int ordinal) => StockPosition(ordinal);
    // Kept for old snapshot template construction; current catalogs never grow returns.
    internal static void ReturnPose(int ordinal, out Vector3 position, out Quaternion rotation)
    { position = new Vector3(0f, 0f, -.2f); rotation = Quaternion.identity; }
}

/// <summary>An authored, decorated open return of the sales counter. The same immutable mesh
/// template is used by observers; only its owner-authored position and fade are transmitted.</summary>
internal sealed class TownServiceMerchantCounter : IDisposable
{
    private readonly GameObject _root;
    private readonly List<Material> _materials = new();
    private readonly Dictionary<Material, Material> _copies = new();
    private readonly TownServiceGrounding _grounding;
    private Transform? _room;
    private Vector3 _position, _scale, _roomScale;
    private Quaternion _rotation;
    private bool _grounded;
    private bool _disposed;
    private float _visibility = -1f;
    internal Transform Root => _root.transform;
    internal Transform Content { get; }
    internal readonly bool Selling;
    internal readonly int Band;
    internal static Transform? Template => TownServiceAssets.Prefab("townmerchant")?.transform.Find("Counter/CounterReturn");

    internal TownServiceMerchantCounter(Transform parent, int placement, bool selling, int band)
    {
        Transform? template = Template;
        if (template == null) throw new InvalidOperationException("The merchant open counter return is missing from the town asset bundle");
        Selling = selling; Band = band;
        TownServiceMerchantLayout.ReturnPose(placement, out Vector3 position, out Quaternion rotation);
        Content = new GameObject("MerchantReturnCards").transform;
        Content.SetParent(parent, false); Content.localPosition = position; Content.localRotation = rotation;
        _root = UnityEngine.Object.Instantiate(template.gameObject, parent, false);
        Root.localPosition = position - Vector3.up * TownServiceMerchantLayout.WorktopHeight;
        Root.localRotation = rotation; Root.localScale = Vector3.one;
        foreach (Collider collider in _root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (Renderer renderer in _root.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material source = materials[i];
                if (source == null) continue;
                if (!_copies.TryGetValue(source, out Material copy))
                { copy = new Material(source); _copies.Add(source, copy); _materials.Add(copy); }
                materials[i] = copy;
            }
            renderer.sharedMaterials = materials;
        }
        _grounding = new TownServiceGrounding(Root, Root);
        VRLayers.Apply(_root);
        SetVisibility(0f); _root.SetActive(true);
    }

    internal void SetVisibility(float visibility)
    {
        Transform? room = SkyAlternative.PlacedRoomRoot;
        Vector3 roomScale = room != null ? room.lossyScale : Vector3.one;
        if (!_grounded || Root.position != _position || Root.rotation != _rotation || Root.lossyScale != _scale
            || room != _room || roomScale != _roomScale)
        {
            _grounding.Resolve(out float actorOffset, out float bottom); _grounding.Apply(actorOffset, bottom);
            _grounded = true; _position = Root.position; _rotation = Root.rotation; _scale = Root.lossyScale;
            _room = room; _roomScale = roomScale;
        }
        visibility = Mathf.Clamp01(visibility);
        if (_visibility == visibility) return;
        _visibility = visibility;
        foreach (Material material in _materials)
            if (material.HasProperty("_TownVisibility")) material.SetFloat("_TownVisibility", visibility);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _grounding.Dispose(); _root.SetActive(false);
        foreach (Material material in _materials) UnityEngine.Object.Destroy(material);
        UnityEngine.Object.Destroy(_root); UnityEngine.Object.Destroy(Content.gameObject);
    }
}
