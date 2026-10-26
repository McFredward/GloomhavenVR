using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.MenuDepth;

/// <summary>
/// Deliberate depth in the original graphic mesh. Layout, native sprite/UV, stencil,
/// colour animation and hit rectangles remain owned by the original UI.
/// </summary>
[DisallowMultipleComponent]
internal sealed class MenuDepthGraphic : BaseMeshEffect, IMenuDepthSource, IMaterialModifier
{
    private readonly List<UIVertex> _input = new(96);
    private readonly List<UIVertex> _stream = new(96);
    private readonly List<UIVertex> _grid = new(4096);
    private readonly List<int> _triangles = new(16000);
    private Image? _image;
    private Sprite? _sprite;
    private MenuDepthHeightfield? _field;
    private float _depthRatio;
    private bool _configured;
    private bool _projected;
    private Vector3 _eye;
    private Material? _baseMaterial, _depthMaterial;
    private static int _refused;

    public Graphic Graphic => graphic;
    public bool IsReady => _configured && IsActive() && _image != null && _sprite != null
        && _image.overrideSprite == _sprite && (_field == null || MenuDepthDisplay.DepthShader != null);

    internal void Configure(Image image, MenuDepthHeightfield? field, float depthRatio)
    {
        Sprite current = image.overrideSprite;
        if (_configured && _image == image && _sprite == current && ReferenceEquals(_field, field)
            && _depthRatio == depthRatio) return;
        _image = image; _sprite = current; _field = field; _depthRatio = depthRatio;
        _configured = current != null;
        _input.Clear(); _grid.Clear(); _triangles.Clear();
        MenuDepthSources.Register(this);
        image.SetVerticesDirty(); image.SetMaterialDirty();
    }

    public void BeforeCapture(MenuDepthEye eye)
    {
        bool project = eye.ProjectToGraphicPlane;
        Vector3 localEye = transform.InverseTransformPoint(eye.WorldPosition);
        if (project != _projected || (project && localEye != _eye))
        {
            _projected = project; _eye = localEye; graphic.SetVerticesDirty();
        }
    }
    public void AfterCapture()
    {
        if (!_projected) return;
        _projected = false;
        if (graphic != null) graphic.SetVerticesDirty();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        if (_configured) MenuDepthSources.Register(this);
    }
    protected override void OnDisable()
    {
        MenuDepthSources.Unregister(this); _projected = false;
        ReleaseMaterial(); base.OnDisable();
        if (graphic != null) graphic.SetMaterialDirty();
    }
    protected override void OnDestroy()
    {
        MenuDepthSources.Unregister(this); ReleaseMaterial(); base.OnDestroy();
    }

    public Material GetModifiedMaterial(Material original)
    {
        if (!IsReady || _field == null) return original;
        Shader? shader = MenuDepthDisplay.DepthShader;
        if (shader == null) return original;
        if (_depthMaterial == null || _baseMaterial != original)
        {
            ReleaseMaterial();
            _baseMaterial = original;
            _depthMaterial = new Material(shader) { name = "GloomhavenVR.MenuDepthGraphic", hideFlags = HideFlags.HideAndDontSave };
        }
        _depthMaterial.CopyPropertiesFromMaterial(original);
        return _depthMaterial;
    }

    public override void ModifyMesh(VertexHelper vertices)
    {
        if (!IsReady || _image == null) return;
        _stream.Clear(); vertices.GetUIVertexStream(_stream);
        if (_stream.Count == 0) return;
        if (_field == null)
        {
            float depth = Mathf.Min(_image.rectTransform.rect.width, _image.rectTransform.rect.height) * _depthRatio;
            vertices.Clear();
            for (int i = 0; i < _stream.Count; i++)
            {
                UIVertex vertex = _stream[i]; vertex.position.z += depth;
                vertex.position = Project(vertex.position); vertices.AddVert(vertex);
            }
            for (int i = 0; i < _stream.Count; i += 3) vertices.AddTriangle(i, i + 1, i + 2);
            return;
        }
        if (!SameInput() && !BuildGrid()) return;
        vertices.Clear();
        for (int i = 0; i < _grid.Count; i++)
        {
            UIVertex vertex = _grid[i]; vertex.position = Project(vertex.position); vertices.AddVert(vertex);
        }
        for (int i = 0; i < _triangles.Count; i += 3)
            vertices.AddTriangle(_triangles[i], _triangles[i + 1], _triangles[i + 2]);
    }

    private Vector3 Project(Vector3 point)
    {
        if (!_projected || _eye.z >= -1e-5f) return point;
        return MenuDepthProjection.ProjectToPlane(point, _eye);
    }

    private bool SameInput()
    {
        if (_input.Count != _stream.Count || _grid.Count == 0) return false;
        for (int i = 0; i < _input.Count; i++)
        {
            UIVertex a = _input[i], b = _stream[i];
            if (a.position != b.position || a.uv0 != b.uv0 || a.uv1 != b.uv1 || a.normal != b.normal
                || a.tangent != b.tangent || a.color.r != b.color.r || a.color.g != b.color.g
                || a.color.b != b.color.b || a.color.a != b.color.a) return false;
        }
        return true;
    }

    private bool BuildGrid()
    {
        _input.Clear(); _grid.Clear(); _triangles.Clear();
        if (_field == null || _stream.Count % 6 != 0 || _stream.Count > 96)
        { Refuse("unsupported native image mesh"); return false; }
        for (int i = 0; i < _stream.Count; i++)
            if (float.IsNaN(_stream[i].uv0.x) || float.IsInfinity(_stream[i].uv0.x)
                || float.IsNaN(_stream[i].uv0.y) || float.IsInfinity(_stream[i].uv0.y))
            { Refuse("non-finite native image UVs"); return false; }
        int columns = Mathf.Clamp(Mathf.CeilToInt(_image!.rectTransform.rect.width / 24f), 8, 64);
        int rows = Mathf.Clamp(Mathf.CeilToInt(_image.rectTransform.rect.height / 24f), 8, 40);
        float depth = _image.rectTransform.rect.width * _depthRatio;
        for (int start = 0; start < _stream.Count; start += 6)
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            for (int i = 0; i < 6; i++)
            {
                Vector3 p = _stream[start + i].position;
                x0 = Mathf.Min(x0, p.x); y0 = Mathf.Min(y0, p.y); x1 = Mathf.Max(x1, p.x); y1 = Mathf.Max(y1, p.y);
            }
            if (x1 - x0 < 1e-5f || y1 - y0 < 1e-5f) { Refuse("degenerate native image mesh"); return false; }
            UIVertex bottomLeft = Corner(start, x0, y0), topLeft = Corner(start, x0, y1);
            UIVertex bottomRight = Corner(start, x1, y0), topRight = Corner(start, x1, y1);
            int first = _grid.Count;
            for (int y = 0; y <= rows; y++) for (int x = 0; x <= columns; x++)
            {
                float u = x / (float)columns, v = y / (float)rows;
                UIVertex vertex = Lerp(Lerp(bottomLeft, bottomRight, u), Lerp(topLeft, topRight, u), v);
                float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
                float t = Mathf.Clamp01(edge / .06f); float feather = t * t * (3f - 2f * t);
                vertex.position.z += depth * (1f - _field.Sample(vertex.uv0.x, vertex.uv0.y)) * feather;
                _grid.Add(vertex);
            }
            for (int y = 0; y < rows; y++) for (int x = 0; x < columns; x++)
            {
                int a = first + y * (columns + 1) + x, b = a + columns + 1;
                _triangles.Add(a); _triangles.Add(b); _triangles.Add(b + 1);
                _triangles.Add(b + 1); _triangles.Add(a + 1); _triangles.Add(a);
            }
        }
        if (_grid.Count > 60000) { _grid.Clear(); Refuse("native image mesh exceeds its vertex bound"); return false; }
        _input.AddRange(_stream);
        return true;
    }

    private UIVertex Corner(int start, float x, float y)
    {
        int best = start; float distance = float.MaxValue;
        for (int i = start; i < start + 6; i++)
        {
            Vector3 p = _stream[i].position; float next = (p.x - x) * (p.x - x) + (p.y - y) * (p.y - y);
            if (next < distance) { best = i; distance = next; }
        }
        return _stream[best];
    }
    private static UIVertex Lerp(UIVertex a, UIVertex b, float t)
    {
        UIVertex result = a;
        result.position = Vector3.LerpUnclamped(a.position, b.position, t);
        result.color = Color32.Lerp(a.color, b.color, t);
        result.uv0 = Vector4.LerpUnclamped(a.uv0, b.uv0, t);
        result.uv1 = Vector4.LerpUnclamped(a.uv1, b.uv1, t);
        result.normal = Vector3.LerpUnclamped(a.normal, b.normal, t);
        result.tangent = Vector4.LerpUnclamped(a.tangent, b.tangent, t);
        return result;
    }
    private void ReleaseMaterial()
    {
        if (_depthMaterial != null) UnityEngine.Object.Destroy(_depthMaterial);
        _depthMaterial = _baseMaterial = null;
    }
    private static void Refuse(string reason)
    {
        if (++_refused <= 4) VRLog.Warn("WorldUI", "MENU DEPTH MESH REFUSED: " + reason + "; native image retained.");
    }
}
