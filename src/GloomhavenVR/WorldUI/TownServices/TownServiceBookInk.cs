using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Typeset native localized copy as ink on the original open book. Each page has
/// its own surface pose; never billboard one unwrapped line above the curved spread.</summary>
internal sealed class TownServiceBookInk
{
    private readonly string _key;
    private readonly Transform _station;
    private readonly bool _remote;
    private Matrix4x4 _stationToWorld, _worldToStation;
    private TMP_Text? _text;
    private PageGeometry? _geometry;
    private static readonly ConditionalWeakTable<Transform, TownServiceBookInk> Remote = new();
    private static readonly ConditionalWeakTable<Transform, PageGeometry> Geometry = new();
    private Transform? _book;
    private bool _fitted;
    private Vector3 _position;
    private Quaternion _rotation;
    private Vector2 _size;

    internal TownServiceBookInk(string key, Transform ritual) { _key = key; _station = ritual.parent; }
    private TownServiceBookInk(string key, Transform station, bool remote) { _key = key; _station = station; _remote = remote; }

    internal static void ApplyRemote(string key, Transform content, Transform stationFrame)
    {
        int separator = key.IndexOf('|');
        if (separator >= 0) key = key.Substring(0, separator);
        if (key != "temple.level" && key != "temple.gold" && key != "temple.progress" && key != "temple.description") return;
        TownServiceBookInk ink = Remote.GetValue(content, _ => new TownServiceBookInk(key, stationFrame, true));
        ink.Apply(content);
    }

    internal void Apply(Transform? content)
    {
        if (content == null) return;
        Transform? book = TownServiceDecor.TempleBookRoot;
        if (book == null) { content.gameObject.SetActive(false); return; }
        if (_book != book)
        {
            _book = book;
            _fitted = Fit(book);
        }
        if (!_fitted) { content.gameObject.SetActive(false); return; }
        content.gameObject.SetActive(true);
        if (_remote)
        {
            // The network root is already at the owner's actual ink pose. Recover its
            // workspace frame from that pose rather than consulting this viewer's NPC
            // position, head, or shared map origin. Additional visitors may stand elsewhere.
            Quaternion rotation = content.rotation * Quaternion.Inverse(_rotation);
            float scale = Mathf.Abs(content.lossyScale.x) * 600f / _size.x;
            Vector3 position = content.position - rotation * (_position * scale);
            _stationToWorld = Matrix4x4.TRS(position, rotation, Vector3.one * scale);
            _worldToStation = _stationToWorld.inverse;
            BindText(content);
            return;
        }
        _stationToWorld = _station.localToWorldMatrix;
        _worldToStation = _station.worldToLocalMatrix;
        content.SetPositionAndRotation(_station.TransformPoint(_position), _station.rotation * _rotation);
        float worldScale = Mathf.Abs(_station.lossyScale.x) * _size.x / 600f;
        float parentScale = content.parent != null ? Mathf.Abs(content.parent.lossyScale.x) : 1f;
        content.localScale = Vector3.one * worldScale / Mathf.Max(.00001f, parentScale);
        TMP_Text? text = content.GetComponent<TMP_Text>() ?? content.GetComponentInChildren<TMP_Text>(true);
        if (text == null) return;
        var rect = text.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        if (rect != content)
        { rect.localPosition = Vector3.zero; rect.localRotation = Quaternion.identity; rect.localScale = Vector3.one; }
        rect.sizeDelta = new Vector2(600f, 600f * _size.y / _size.x);
        text.raycastTarget = false;
        text.color = new Color(.12f, .065f, .027f, 1f);
        text.overrideColorTags = true;
        text.enableWordWrapping = true;
        text.enableAutoSizing = true;
        text.fontSizeMin = 28f; text.fontSizeMax = _key == "temple.description" ? 38f : 55f;
        text.alignment = _key == "temple.description" ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.Center;
        text.overflowMode = TextOverflowModes.Overflow;
        text.margin = Vector4.zero;
        content.SetPositionAndRotation(_station.TransformPoint(_position), _station.rotation * _rotation);
        BindText(content);
    }

    private void BindText(Transform content)
    {
        TMP_Text? text = content.GetComponent<TMP_Text>() ?? content.GetComponentInChildren<TMP_Text>(true);
        if (text == null) return;
        text.overrideColorTags = true;
        if (_text != text)
        {
            if (_text != null) _text.OnPreRenderText -= ProjectVertices;
            _text = text;
            _text.OnPreRenderText += ProjectVertices;
            _text.SetVerticesDirty();
        }
    }

    private bool Fit(Transform book)
    {
        // Coordinates use the existing original book's measured 30 cm spread. Separate
        // pages keep the spine free, and the body copy wraps inside the right page.
        Vector3 point = _key switch
        {
            "temple.level" => new Vector3(-.407f, 0f, -.059f),
            "temple.gold" => new Vector3(-.407f, 0f, -.109f),
            "temple.progress" => new Vector3(-.407f, 0f, -.163f),
            _ => new Vector3(-.253f, 0f, -.12f)
        };
        _size = _key == "temple.description" ? new Vector2(.116f, .16f) : new Vector2(.114f, .039f);
        if (_station == null) return false;
        _geometry = Geometry.GetValue(book, source => new PageGeometry(source));
        if (!_geometry.Sample(point.x, point.z, out Vector3 surface, out Vector3 normal)) return false;
        _position = surface + normal * .00065f;
        _rotation = Quaternion.LookRotation(-normal, Vector3.ProjectOnPlane(Vector3.forward, normal));
        return true;
    }

    private void ProjectVertices(TMP_TextInfo info)
    {
        if (_text == null || _geometry == null || _station == null) return;
        for (int i = 0; i < info.characterCount; i++)
        {
            TMP_CharacterInfo glyph = info.characterInfo[i];
            if (!glyph.isVisible) continue;
            Vector3[] vertices = info.meshInfo[glyph.materialReferenceIndex].vertices;
            for (int corner = 0; corner < 4; corner++)
            {
                int index = glyph.vertexIndex + corner;
                Vector3 point = _worldToStation.MultiplyPoint3x4(_text.transform.TransformPoint(vertices[index]));
                if (!_geometry.Sample(point.x, point.z, out Vector3 surface, out Vector3 normal)) continue;
                vertices[index] = _text.transform.InverseTransformPoint(_stationToWorld.MultiplyPoint3x4(surface + normal * .00065f));
            }
        }
    }

    // Cache read-only original triangles once. This creates no physics geometry, so book
    // lettering can never become an invisible laser occluder. Projection runs only when TMP
    // regenerates its glyphs; native numbers/text changes keep their ordinary update cadence.
    private sealed class PageGeometry
    {
        private readonly List<(Vector3 A, Vector3 B, Vector3 C, Vector3 Normal)> _triangles = new();
        internal PageGeometry(Transform book)
        {
            Transform frame = book.parent != null ? book.parent : book;
            foreach (MeshFilter filter in book.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null || !mesh.isReadable) continue;
                Vector3[] vertices = mesh.vertices; int[] indices = mesh.triangles;
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    Vector3 a = frame.InverseTransformPoint(filter.transform.TransformPoint(vertices[indices[i]]));
                    Vector3 b = frame.InverseTransformPoint(filter.transform.TransformPoint(vertices[indices[i + 1]]));
                    Vector3 c = frame.InverseTransformPoint(filter.transform.TransformPoint(vertices[indices[i + 2]]));
                    Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                    if (normal.y < 0f) normal = -normal;
                    if (normal.y >= .6f) _triangles.Add((a, b, c, normal));
                }
            }
        }
        internal bool Sample(float x, float z, out Vector3 point, out Vector3 normal)
        {
            point = default; normal = Vector3.up; float height = float.NegativeInfinity;
            Vector3 origin = new Vector3(x, 3f, z);
            foreach (var t in _triangles)
            {
                if (x < Mathf.Min(t.A.x, Mathf.Min(t.B.x, t.C.x)) || x > Mathf.Max(t.A.x, Mathf.Max(t.B.x, t.C.x))
                    || z < Mathf.Min(t.A.z, Mathf.Min(t.B.z, t.C.z)) || z > Mathf.Max(t.A.z, Mathf.Max(t.B.z, t.C.z))) continue;
                if (!Intersect(origin, Vector3.down, t.A, t.B, t.C, out float distance)) continue;
                float y = origin.y - distance;
                if (y <= height) continue;
                height = y; point = new Vector3(x, y, z); normal = t.Normal;
            }
            return !float.IsNegativeInfinity(height);
        }
    }

    internal static bool Intersect(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, out float distance)
    {
        distance = 0f;
        Vector3 edge1 = b - a, edge2 = c - a, p = Vector3.Cross(direction, edge2);
        float det = Vector3.Dot(edge1, p);
        if (Mathf.Abs(det) < 1e-9f) return false;
        float inv = 1f / det;
        Vector3 t = origin - a;
        float u = Vector3.Dot(t, p) * inv;
        if (u < 0f || u > 1f) return false;
        Vector3 q = Vector3.Cross(t, edge1);
        float v = Vector3.Dot(direction, q) * inv;
        if (v < 0f || u + v > 1f) return false;
        distance = Vector3.Dot(edge2, q) * inv;
        return distance >= 0f;
    }
}
