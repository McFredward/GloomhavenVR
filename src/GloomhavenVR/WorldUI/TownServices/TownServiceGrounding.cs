using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Stable native map-floor contacts resolved by the resident author, never by observers.
/// Separate authored supports retain their top anchors; transform-only changes mirror exactly.</summary>
internal sealed class TownServiceGrounding : IDisposable
{
    private readonly Transform _station;
    private readonly Transform? _actor;
    private readonly Vector3 _actorRest;
    private readonly List<Support> _supports = new();
    private readonly List<Vector3> _footprint = new();
    private bool _applied, _disposed;
    private float _actorOffset, _furnitureBottom;

    private sealed class Support
    {
        internal readonly Transform Part;
        internal readonly Vector3 Position, Scale, TopPoint;
        internal readonly float Bottom, Top;
        internal readonly int Axis;
        internal Support(Transform station, MeshFilter filter)
        {
            Part = filter.transform; Position = Part.localPosition; Scale = Part.localScale;
            Vector3[] vertices = filter.sharedMesh.vertices;
            Bottom = float.PositiveInfinity; Top = float.NegativeInfinity; TopPoint = Vector3.zero;
            Matrix4x4 toStation = station.worldToLocalMatrix * Part.localToWorldMatrix;
            foreach (Vector3 vertex in vertices)
            {
                float y = toStation.MultiplyPoint3x4(vertex).y;
                Bottom = Mathf.Min(Bottom, y);
                if (y > Top) { Top = y; TopPoint = vertex; }
            }
            float alignment = 0f;
            for (int axis = 0; axis < 3; axis++)
            {
                Vector3 direction = Vector3.zero; direction[axis] = 1f;
                float dot = Mathf.Abs(Vector3.Dot(Part.TransformDirection(direction).normalized, station.up));
                if (dot > alignment) { alignment = dot; Axis = axis; }
            }
            if (alignment < .999f || Top - Bottom < .001f)
                throw new InvalidOperationException("Authored support needs a vertical local axis and nonzero height");
        }
        internal void Apply(float bottom)
        {
            if (Part == null) return;
            Part.localPosition = Position; Part.localScale = Scale;
            Vector3 anchor = Part.TransformPoint(TopPoint);
            Vector3 scale = Scale;
            scale[Axis] *= Mathf.Max(.001f, Top - bottom) / (Top - Bottom);
            Part.localScale = scale;
            Part.position += anchor - Part.TransformPoint(TopPoint);
        }
        internal void Restore()
        { if (Part != null) { Part.localPosition = Position; Part.localScale = Scale; } }
    }

    internal TownServiceGrounding(Transform station, Transform? furnitureRoot = null)
    {
        _station = station;
        _actor = station.Find("Actor");
        _actorRest = _actor != null ? _actor.localPosition : Vector3.zero;
        Transform? furniture = furnitureRoot ?? station.Find("Counter") ?? station.Find("Shrine") ?? station.Find("Workbench");
        if (furniture == null) return;
        foreach (MeshFilter filter in furniture.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!filter.name.StartsWith("GroundSupport", StringComparison.Ordinal)) continue;
            bool active = true;
            for (Transform? part = filter.transform; part != null && part != furniture; part = part.parent)
                if (!part.gameObject.activeSelf) { active = false; break; }
            if (!active || filter.sharedMesh == null) continue;
            try
            {
                if (!filter.sharedMesh.isReadable) throw new InvalidOperationException("Authored support mesh is not readable");
                var support = new Support(station, filter);
                _supports.Add(support);
                Bounds bounds = filter.sharedMesh.bounds;
                // Actual contacting mesh footprint, independent of primitive names or cube size.
                // All eight bounds corners account for FBX axis conversion; project to the sole.
                foreach (float x in new[] { bounds.min.x, bounds.max.x })
                    foreach (float y in new[] { bounds.min.y, bounds.max.y })
                        foreach (float z in new[] { bounds.min.z, bounds.max.z })
                        {
                            Vector3 point = station.InverseTransformPoint(filter.transform.TransformPoint(new Vector3(x, y, z)));
                            point.y = support.Bottom; _footprint.Add(point);
                        }
            }
            catch (Exception error)
            { VRLog.Warn("TownServices", "Furniture support cannot be sampled: " + filter.name + "; " + error.Message); }
        }
    }

    internal void Resolve(out float actorOffset, out float furnitureBottom)
    {
        actorOffset = furnitureBottom = 0f;
        if (!TownServicePlacement.TryCanonicalFloor(out float floor)) return;
        if (_actor != null)
            actorOffset = Mathf.Min(Height(floor, new Vector3(-.12f, 0f, .65f)),
                Height(floor, new Vector3(.12f, 0f, .65f)));
        if (_footprint.Count == 0) return;
        furnitureBottom = float.PositiveInfinity;
        foreach (Vector3 point in _footprint) furnitureBottom = Mathf.Min(furnitureBottom, Height(floor, point));
    }

    private float Height(float floor, Vector3 local)
    {
        Vector3 world = _station.TransformPoint(local);
        world.y = floor;
        return _station.InverseTransformPoint(world).y;
    }

    internal void Apply(float actorOffset, float furnitureBottom)
    {
        if (_disposed || !Finite(actorOffset) || !Finite(furnitureBottom)) return;
        if (_applied && actorOffset == _actorOffset && furnitureBottom == _furnitureBottom) return;
        _applied = true; _actorOffset = actorOffset; _furnitureBottom = furnitureBottom;
        if (_actor != null) _actor.localPosition = _actorRest + Vector3.up * actorOffset;
        foreach (Support support in _supports) support.Apply(furnitureBottom);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_actor != null) _actor.localPosition = _actorRest;
        foreach (Support support in _supports) support.Restore();
        _supports.Clear(); _footprint.Clear();
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
