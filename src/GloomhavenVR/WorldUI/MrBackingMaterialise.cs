using System;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// The MR plate participates in its window's existing spatial erosion field. A primitive quad
/// cannot express a ragged front and has no CanvasRenderer alpha, so animate one bounded owned
/// grid with the runner's element progress. There is no second clock or debris tail here.
/// Renderer/object visibility belongs to MrBacking; this helper never enables either of them.
/// </summary>
internal sealed class MrBackingMaterialise : IDisposable
{
    private const int Cells = 32;
    private const int Side = Cells + 1;
    private const int VertexCount = Side * Side;
    private Mesh? _mesh;
    private Color[]? _colors;
    private float[]? _thresholds;
    private Transform? _plate;
    private MeshFilter? _filter;
    private Renderer? _renderer;
    private Mesh? _originalMesh;
    private Material? _originalMaterial;
    private Material? _fadeMaterial;
    private Rect _shown;
    private Rect _hostFrame;
    private bool _haveThresholds;
    private float _progress = -1f;
    private bool _disposed;

    /// <summary>
    /// <paramref name="shown"/> is the fitted plate rect in host coordinates. The host frame
    /// must be the SAME rect captured by the runner for its native elements, not a newer layout.
    /// The caller supplies the existing alpha-blended, non-depth-writing plate material and
    /// remains responsible for hiding the plate when element progress reaches exactly one.
    /// </summary>
    internal void Apply(Transform plate, Rect shown, Rect hostFrame, float elementProgress,
                        Material fadeMaterial)
    {
        if (_disposed || plate == null)
            return;
        if (!Finite(elementProgress) || !Valid(shown) || !Valid(hostFrame) || elementProgress <= 0f)
        {
            RestoreBoundPlate();
            return;
        }
        if (_plate != plate)
        {
            RestoreBoundPlate();
            _filter = plate.GetComponent<MeshFilter>();
            _renderer = plate.GetComponent<Renderer>();
            if (_filter == null || _renderer == null)
                return;
            _plate = plate;
            _originalMesh = _filter.sharedMesh;
            _originalMaterial = _renderer.sharedMaterial;
        }
        if (_filter == null || _renderer == null)
            return;
        EnsureMesh();
        bool changed = !_haveThresholds || !Same(_shown, shown) || !Same(_hostFrame, hostFrame);
        if (changed)
        {
            _shown = shown;
            _hostFrame = hostFrame;
            float aspect = hostFrame.width / Mathf.Max(hostFrame.height, 0.01f);
            for (int y = 0; y < Side; y++)
            {
                float v = (float)y / Cells;
                for (int x = 0; x < Side; x++)
                {
                    float u = (float)x / Cells;
                    // Same clamped host UV as WindowMaterialiseRunner.UvOf, including backing
                    // margins outside host.rect. Using 0..1 over the plate itself would move the
                    // front when a fitted rect is offset or smaller than its transparent host.
                    Vector2 uv = new(
                        Mathf.Clamp01((shown.xMin + u * shown.width - hostFrame.xMin) / hostFrame.width),
                        Mathf.Clamp01((shown.yMin + v * shown.height - hostFrame.yMin) / hostFrame.height));
                    _thresholds![y * Side + x] = WindowMaterialiseField.Threshold(uv, aspect);
                }
            }
            _haveThresholds = true;
        }
        float progress = Mathf.Clamp01(elementProgress);
        if (changed || _progress != progress)
        {
            for (int i = 0; i < VertexCount; i++)
                _colors![i] = new Color(1f, 1f, 1f,
                    WindowMaterialiseField.Presence(_thresholds![i], progress));
            _mesh!.colors = _colors!;
            _progress = progress;
        }
        if (_filter.sharedMesh != _mesh)
            _filter.sharedMesh = _mesh;
        if (_renderer.sharedMaterial != fadeMaterial)
            _renderer.sharedMaterial = fadeMaterial;
        _fadeMaterial = fadeMaterial;
    }

    private void EnsureMesh()
    {
        if (_mesh != null)
            return;
        var vertices = new Vector3[VertexCount];
        var normals = new Vector3[VertexCount];
        var uv = new Vector2[VertexCount];
        var triangles = new int[Cells * Cells * 6];
        _colors = new Color[VertexCount];
        _thresholds = new float[VertexCount];
        for (int y = 0; y < Side; y++)
        {
            for (int x = 0; x < Side; x++)
            {
                int i = y * Side + x;
                float u = (float)x / Cells, v = (float)y / Cells;
                vertices[i] = new Vector3(u - 0.5f, v - 0.5f, 0f);
                normals[i] = Vector3.back;
                uv[i] = new Vector2(u, v);
                if (x == Cells || y == Cells)
                    continue;
                int t = (y * Cells + x) * 6;
                triangles[t] = i;
                triangles[t + 1] = i + Side;
                triangles[t + 2] = i + 1;
                triangles[t + 3] = i + 1;
                triangles[t + 4] = i + Side;
                triangles[t + 5] = i + Side + 1;
            }
        }
        _mesh = new Mesh { name = "GloomhavenVR.MrBackingMaterialise", hideFlags = HideFlags.HideAndDontSave };
        _mesh.MarkDynamic();
        _mesh.vertices = vertices;
        _mesh.normals = normals;
        _mesh.uv = uv;
        _mesh.triangles = triangles;
        _mesh.bounds = new Bounds(Vector3.zero, new Vector3(1f, 1f, 0.01f));
        _haveThresholds = false;
        _progress = -1f;
    }

    /// <summary>Restore borrowed presentation; keep the owned mesh for the next opening.</summary>
    internal void Restore(Transform? plate)
    {
        if (plate == null || ReferenceEquals(_plate, plate))
            RestoreBoundPlate();
    }

    private void RestoreBoundPlate()
    {
        if (_filter != null && _filter.sharedMesh == _mesh)
            _filter.sharedMesh = _originalMesh;
        if (_renderer != null && _renderer.sharedMaterial == _fadeMaterial)
            _renderer.sharedMaterial = _originalMaterial;
        _plate = null;
        _filter = null;
        _renderer = null;
        _originalMesh = null;
        _originalMaterial = null;
        _fadeMaterial = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        RestoreBoundPlate();
        if (_mesh != null)
            UnityEngine.Object.Destroy(_mesh);
        _mesh = null;
        _colors = null;
        _thresholds = null;
        _disposed = true;
    }

    private static bool Same(Rect a, Rect b) =>
        a.xMin == b.xMin && a.yMin == b.yMin && a.width == b.width && a.height == b.height;

    private static bool Valid(Rect r) => Finite(r.xMin) && Finite(r.yMin)
        && Finite(r.width) && Finite(r.height) && r.width > 1e-5f && r.height > 1e-5f;

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
