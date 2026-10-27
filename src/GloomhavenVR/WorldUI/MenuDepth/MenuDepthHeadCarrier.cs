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
    private readonly GameObject _owner;
    private readonly Mesh _mesh;
    private readonly MeshRenderer _renderer;
    private bool _hidden, _wasCulled;
    internal MenuDepthHeadCarrier(RawImage original, Material material) : this(original.transform, material, original) { }
    internal MenuDepthHeadCarrier(Transform original, Material material) : this(original, material, null) { }
    private MenuDepthHeadCarrier(Transform original, Material material, RawImage? image)
    {
        _original = image; _transform = original;
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
        if (image != null && image.canvas != null)
        { _renderer.sortingLayerID = image.canvas.sortingLayerID; _renderer.sortingOrder = image.canvas.sortingOrder; }
        else if (_originalRenderer != null)
        { _renderer.sortingLayerID = _originalRenderer.sortingLayerID; _renderer.sortingOrder = _originalRenderer.sortingOrder; }
        _renderer.enabled = false;
    }
    internal void Draw(Camera head)
    {
        if (_transform == null || !_transform.gameObject.activeInHierarchy || (_original != null && !_original.isActiveAndEnabled)) return;
        float scale = Mathf.Max(1e-8f, Mathf.Min(_owner.transform.lossyScale.x,
            Mathf.Min(_owner.transform.lossyScale.y, _owner.transform.lossyScale.z)));
        _mesh.bounds = new Bounds(_owner.transform.InverseTransformPoint(head.transform.position),
            Vector3.one * (head.farClipPlane * 4f / scale));
        if (!_hidden) { _wasCulled = _original != null ? _original.canvasRenderer.cull : _originalRenderer != null && !_originalRenderer.enabled; _hidden = true; }
        if (_original != null) _original.canvasRenderer.cull = true;
        if (_originalRenderer != null) _originalRenderer.enabled = false;
        _renderer.enabled = true;
    }
    internal void EndDraw()
    {
        if (_renderer != null) _renderer.enabled = false;
        if (_hidden && _original != null) _original.canvasRenderer.cull = _wasCulled;
        if (_hidden && _originalRenderer != null) _originalRenderer.enabled = !_wasCulled;
        _hidden = false;
    }
    public void Dispose()
    { EndDraw(); UnityEngine.Object.Destroy(_owner); UnityEngine.Object.Destroy(_mesh); }
}
