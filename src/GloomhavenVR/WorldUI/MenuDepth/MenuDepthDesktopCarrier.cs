using System;
using UnityEngine;

namespace GloomhavenVR.WorldUI.MenuDepth;

/// <summary>Visual-only whole-frame carrier; the original screen remains the input/pose authority.</summary>
internal sealed class MenuDepthDesktopCarrier : IDisposable
{
    private readonly Transform _screen;
    private readonly Renderer _original;
    private readonly GameObject _object;
    private readonly Mesh _mesh;
    private readonly Material _material;
    private bool _hidden, _wasEnabled;
    private Rect _frame;

    internal MenuDepthDesktopCarrier(Transform screen, Material originalMaterial, RenderTexture left, RenderTexture right)
    {
        _screen = screen; _original = screen.GetComponent<Renderer>();
        if (_original == null) throw new InvalidOperationException("The original menu glass has no visual owner.");
        _material = MenuDepthDisplay.CreateDisplayMaterial(left, right);
        // Mesh glass has no uGUI stencil/color-mask contract to copy. Preserve
        // its actual tint and bind depth locally instead of importing absent fields.
        if (originalMaterial.HasProperty("_Color")) _material.SetColor("_Color", originalMaterial.GetColor("_Color"));
        _material.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
        _material.renderQueue = originalMaterial.renderQueue;
        _object = new GameObject("GloomhavenVR.FlatScreen.MenuDepthCarrier", typeof(MeshFilter), typeof(MeshRenderer));
        _object.layer = screen.gameObject.layer; _object.transform.SetParent(screen, false);
        _object.hideFlags = HideFlags.HideAndDontSave; _object.SetActive(false);
        _mesh = new Mesh { name = "GloomhavenVR.FlatScreen.MenuDepthCarrier", hideFlags = HideFlags.HideAndDontSave };
        _object.GetComponent<MeshFilter>().sharedMesh = _mesh;
        var renderer = _object.GetComponent<MeshRenderer>(); renderer.sharedMaterial = _material;
        renderer.sortingLayerID = _original.sortingLayerID; renderer.sortingOrder = _original.sortingOrder;
    }

    internal void Draw(RenderTexture selected, Rect frame, float physicalDepthPerRawDepth)
    {
        if (frame != _frame)
        {
            _frame = frame;
            _mesh.vertices = new[] { new Vector3(frame.xMin-.5f,frame.yMin-.5f), new Vector3(frame.xMin-.5f,frame.yMax-.5f),
                new Vector3(frame.xMax-.5f,frame.yMax-.5f), new Vector3(frame.xMax-.5f,frame.yMin-.5f) };
            _mesh.uv = new[] { new Vector2(0,0), new Vector2(0,1), new Vector2(1,1), new Vector2(1,0) };
            _mesh.colors32 = new[] { new Color32(255,255,255,255),new Color32(255,255,255,255),new Color32(255,255,255,255),new Color32(255,255,255,255) };
            _mesh.triangles = new[] { 0,1,2,0,2,3 }; _mesh.RecalculateBounds();
        }
        Rect originalUv = new(-frame.x / frame.width, -frame.y / frame.height, 1f / frame.width, 1f / frame.height);
        Vector3 normal = Vector3.Cross(_screen.TransformVector(Vector3.right).normalized,
            _screen.TransformVector(Vector3.up).normalized).normalized;
        _material.mainTexture = selected; _material.SetTexture("_RightTex", selected);
        MenuDepthDisplay.SetCaptureWindow(_material, selected, selected, originalUv, new Vector2(.5f,.5f),
            new Vector2(physicalDepthPerRawDepth,physicalDepthPerRawDepth), _screen.position, normal);
        if (!_hidden) { _wasEnabled = _original.enabled; _hidden = true; }
        _original.enabled = false; _object.SetActive(true);
    }

    internal void EndDraw()
    {
        if (_object != null) _object.SetActive(false);
        if (_hidden && _original != null) _original.enabled = _wasEnabled;
        _hidden = false;
    }
    public void Dispose()
    {
        EndDraw(); UnityEngine.Object.Destroy(_object); UnityEngine.Object.Destroy(_mesh);
        MenuDepthDisplay.ReleaseDisplayMaterial(_material);
    }
}
