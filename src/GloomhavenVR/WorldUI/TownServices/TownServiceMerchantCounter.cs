using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Measured open-counter slots. Every card has an exposed face and its own collider;
/// stock growth adds furnished side returns instead of replacing a page or closing a drawer.</summary>
internal static class TownServiceMerchantLayout
{
    internal const int StockColumns = 24, StockRows = 8;
    internal const int StockCapacity = StockColumns * StockRows;
    internal const int ReturnColumns = 8, ReturnRows = 8;
    internal const int ReturnCapacity = ReturnColumns * ReturnRows;
    internal const float CardWidth = .14f, CardHeight = .112f, FacePitch = 65f;
    internal const float WorktopHeight = .970f, ColumnPitch = .15f, RowPitch = .13f, TerraceRise = .008f;
    // The tilted card's lower edge and original price strip must clear the terrace's
    // retaining lip. WorktopHeight remains the native transaction/action surface.
    internal const float CardSeatLift = .034f;

    internal static Vector3 StockPosition(int ordinal)
    {
        float x = (ordinal % StockColumns - 11.5f) * ColumnPitch;
        int row = ordinal / StockColumns;
        float bow = x / 1.725f;
        // Keep the rear edge in front of the NPC's ledger/coin workspace (z >= 0).
        return new Vector3(x, CardSeatLift + row * TerraceRise, -1.32f + row * RowPitch + .16f * bow * bow);
    }

    internal static Vector3 ReturnPosition(int ordinal) => new Vector3(
        (ordinal % ReturnColumns - 3.5f) * ColumnPitch, CardSeatLift + ordinal / ReturnColumns * TerraceRise,
        -.455f + ordinal / ReturnColumns * RowPitch);

    internal static void ReturnPose(int ordinal, out Vector3 position, out Quaternion rotation)
    {
        int pair = ordinal / 2;
        float side = ordinal % 2 == 0 ? -1f : 1f;
        position = new Vector3(side * (2.85f + .25f * pair), 0f, -.25f + pair * 1.40f);
        rotation = Quaternion.Euler(0f, -side * 10f, 0f);
    }
}

/// <summary>An authored, decorated open return of the sales counter. The same immutable mesh
/// template is used by observers; only its owner-authored position and fade are transmitted.</summary>
internal sealed class TownServiceMerchantCounter : IDisposable
{
    private readonly GameObject _root;
    private readonly List<Material> _materials = new();
    private readonly Dictionary<Material, Material> _copies = new();
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
        VRLayers.Apply(_root);
        SetVisibility(0f); _root.SetActive(true);
    }

    internal void SetVisibility(float visibility)
    {
        visibility = Mathf.Clamp01(visibility);
        if (_visibility == visibility) return;
        _visibility = visibility;
        foreach (Material material in _materials)
            if (material.HasProperty("_TownVisibility")) material.SetFloat("_TownVisibility", visibility);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _root.SetActive(false);
        foreach (Material material in _materials) UnityEngine.Object.Destroy(material);
        UnityEngine.Object.Destroy(_root); UnityEngine.Object.Destroy(Content.gameObject);
    }
}
