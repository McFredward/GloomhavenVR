using System;
using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Preserve furniture proportions and individual welded tree shapes while the
/// decorative room shell opens its larger clearing. Never modifies an authored mesh.</summary>
internal sealed class TownServiceRoomGeometry : IDisposable
{
    private readonly List<(Transform transform, Vector3 scale)> _props = new();
    private readonly List<IslandMesh> _trees = new();
    private float _factor = 1f;
    private static readonly string[] PropPrefixes = { "Barrel", "Table", "Stool", "Shelf", "Bucket", "Jug",
        "Log", "LeanTree", "Stump", "Axe", "Rocks", "Branches", "Fern", "Shrub" };

    internal TownServiceRoomGeometry(Transform room)
    {
        Transform? geometry = room.Find("RoomGeo");
        if (geometry == null) return;
        foreach (Transform child in geometry)
        {
            if (child.name == "TrunksNear" || child.name == "TrunksFar")
            {
                MeshFilter? filter = child.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                    _trees.Add(new IslandMesh(filter));
                continue;
            }
            foreach (string prefix in PropPrefixes)
                if (child.name.StartsWith(prefix, StringComparison.Ordinal))
                { _props.Add((child, child.localScale)); break; }
        }
    }

    internal void Apply(float factor)
    {
        if (Mathf.Approximately(_factor, factor)) return;
        _factor = factor;
        foreach (var prop in _props)
            if (prop.transform != null)
                prop.transform.localScale = new Vector3(prop.scale.x / factor, prop.scale.y, prop.scale.z / factor);
        foreach (IslandMesh tree in _trees) tree.Apply(factor);
    }

    public void Dispose()
    {
        foreach (var prop in _props)
            if (prop.transform != null) prop.transform.localScale = prop.scale;
        foreach (IslandMesh tree in _trees) tree.Dispose();
        _props.Clear(); _trees.Clear();
    }

    /// <summary>The shipped trunk meshes contain disconnected complete trees. Translate
    /// each component around its own centre; retain every edge, UV, normal and submesh.</summary>
    private sealed class IslandMesh : IDisposable
    {
        private readonly MeshFilter _filter;
        private readonly Mesh _original, _copy;
        private readonly Vector3 _scale;
        private readonly Vector3[] _vertices, _centres, _output;
        internal IslandMesh(MeshFilter filter)
        {
            _filter = filter; _original = filter.sharedMesh; _scale = filter.transform.localScale;
            if (!_original.isReadable)
                throw new InvalidOperationException("Town room trunk mesh is not readable: " + _original.name);
            _vertices = _original.vertices;
            _centres = ComponentCentres(_vertices, _original.triangles);
            _output = new Vector3[_vertices.Length];
            _copy = UnityEngine.Object.Instantiate(_original); _copy.name = _original.name + " TownClearance";
        }
        internal void Apply(float factor)
        {
            if (_filter == null) return;
            if (Mathf.Approximately(factor, 1f))
            { _filter.sharedMesh = _original; _filter.transform.localScale = _scale; return; }
            for (int i = 0; i < _vertices.Length; i++)
                _output[i] = _vertices[i] + new Vector3(_centres[i].x * (factor - 1f), 0f, _centres[i].z * (factor - 1f));
            _copy.vertices = _output; _copy.RecalculateBounds(); _filter.sharedMesh = _copy;
            _filter.transform.localScale = new Vector3(_scale.x / factor, _scale.y, _scale.z / factor);
        }
        public void Dispose()
        {
            if (_filter != null) { _filter.sharedMesh = _original; _filter.transform.localScale = _scale; }
            if (_copy != null) UnityEngine.Object.Destroy(_copy);
        }
    }

    internal static Vector3[] ComponentCentres(Vector3[] vertices, int[] triangles)
    {
        int[] parents = new int[vertices.Length];
        for (int i = 0; i < parents.Length; i++) parents[i] = i;
        int Find(int value)
        {
            while (parents[value] != value) { parents[value] = parents[parents[value]]; value = parents[value]; }
            return value;
        }
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int root = Find(triangles[i]);
            parents[Find(triangles[i + 1])] = root; parents[Find(triangles[i + 2])] = root;
        }
        var bounds = new Dictionary<int, Bounds>();
        for (int i = 0; i < vertices.Length; i++)
        {
            int root = Find(i);
            if (!bounds.TryGetValue(root, out Bounds value)) value = new Bounds(vertices[i], Vector3.zero);
            else value.Encapsulate(vertices[i]);
            bounds[root] = value;
        }
        var centres = new Vector3[vertices.Length];
        for (int i = 0; i < vertices.Length; i++) centres[i] = bounds[Find(i)].center;
        return centres;
    }
}
