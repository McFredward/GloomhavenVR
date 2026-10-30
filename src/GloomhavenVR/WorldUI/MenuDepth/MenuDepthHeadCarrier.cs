using System;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.MenuDepth;

// A head-raster is an image of fixed world geometry, not a moved world window.
// Its full-screen mesh is only a lossless presentation carrier. Input, native
// layout and shared pose remain on their original transforms.
internal sealed class MenuDepthHeadCarrier : IDisposable
{
    private readonly RawImage? _original;
    private readonly Renderer? _originalRenderer;
    private readonly Transform _transform;
    private readonly Canvas? _photoCanvas;
    private readonly bool _canvasEnabled;
    private bool _disposed;
    private readonly GameObject _owner;
    private readonly Mesh _mesh;
    private readonly MeshRenderer _renderer;
    private bool _hidden, _rendererEnabled;
    internal MenuDepthHeadCarrier(RawImage original, Material material) : this(original.transform, material, original) { }
    internal MenuDepthHeadCarrier(Transform original, Material material) : this(original, material, null) { }
    private MenuDepthHeadCarrier(Transform original, Material material, RawImage? image)
    {
        _original = image; _transform = original;
        if (image != null)
        {
            // BuildDisplay owns this exact same-object, one-photograph Canvas.
            // Never walk to an original game HostCanvas or alter its widgets.
            _photoCanvas = image.GetComponent<Canvas>()
                ?? throw new InvalidOperationException("The mod-owned photograph requires its own Canvas.");
            _canvasEnabled = _photoCanvas.enabled;
        }
        _originalRenderer = image == null ? original.GetComponent<Renderer>() : null;
        _owner = new GameObject("GloomhavenVR.MenuDepth.HeadRaster", typeof(MeshFilter), typeof(MeshRenderer));
        _owner.hideFlags = HideFlags.HideAndDontSave;
        _owner.layer = original.gameObject.layer;
        _owner.transform.SetParent(original, false);
        _mesh = new Mesh { name = "GloomhavenVR.MenuDepth.HeadRaster", hideFlags = HideFlags.HideAndDontSave };
        _mesh.vertices = new[] { new Vector3(-1,-1), new Vector3(-1,1), new Vector3(1,1), new Vector3(1,-1) };
        _mesh.uv = new[] { new Vector2(0,0), new Vector2(0,1), new Vector2(1,1), new Vector2(1,0) };
        _mesh.colors32 = new[] { new Color32(255,255,255,255),new Color32(255,255,255,255),new Color32(255,255,255,255),new Color32(255,255,255,255) };
        _mesh.triangles = new[] { 0,1,2,0,2,3 };
        _owner.GetComponent<MeshFilter>().sharedMesh = _mesh;
        _renderer = _owner.GetComponent<MeshRenderer>(); _renderer.sharedMaterial = material;
        RefreshOrder();
        _renderer.enabled = false;
        // A world-space Canvas can submit its cached mono batch after a head
        // post-render callback. Per-eye cull/alpha/component/layer leases all
        // reproduced the extra mono-shaped ink. Retire only this private proxy
        // Canvas for the head presentation's lifetime; dispose restores it.
        if (_photoCanvas != null) _photoCanvas.enabled = false;
    }
    internal void Draw(Camera head)
    {
        if (_disposed || _transform == null || !_transform.gameObject.activeInHierarchy || (_original != null && !_original.isActiveAndEnabled)) return;
        RefreshOrder();
        float scale = Mathf.Max(1e-8f, Mathf.Min(_owner.transform.lossyScale.x,
            Mathf.Min(_owner.transform.lossyScale.y, _owner.transform.lossyScale.z)));
        _mesh.bounds = new Bounds(_owner.transform.InverseTransformPoint(head.transform.position),
            Vector3.one * (head.farClipPlane * 4f / scale));
        if (!_hidden)
        {
            _rendererEnabled = _originalRenderer != null && _originalRenderer.enabled;
            _hidden = true;
        }
        if (_originalRenderer != null) _originalRenderer.enabled = false;
        _renderer.enabled = true;
    }
    internal void EndDraw()
    {
        if (_renderer != null) _renderer.enabled = false;
        if (_hidden && _originalRenderer != null) _originalRenderer.enabled = _rendererEnabled;
        _hidden = false;
    }
    internal bool HasHeadPresentation => !_disposed && _photoCanvas != null && !_photoCanvas.enabled
        && _transform != null && _transform.gameObject.activeInHierarchy;
    private void RefreshOrder()
    {
        int layer = _photoCanvas != null ? _photoCanvas.sortingLayerID : _originalRenderer != null ? _originalRenderer.sortingLayerID : _renderer.sortingLayerID;
        int order = _photoCanvas != null ? _photoCanvas.sortingOrder : _originalRenderer != null ? _originalRenderer.sortingOrder : _renderer.sortingOrder;
        if (_renderer.sortingLayerID != layer) _renderer.sortingLayerID = layer;
        if (_renderer.sortingOrder != order) _renderer.sortingOrder = order;
    }
    public void Dispose()
    {
        if (_disposed) return;
        EndDraw(); _disposed = true;
        // A foreign later enable is authoritative; only undo our own false.
        if (_photoCanvas != null && !_photoCanvas.enabled) _photoCanvas.enabled = _canvasEnabled;
        UnityEngine.Object.Destroy(_owner); UnityEngine.Object.Destroy(_mesh);
    }
}
