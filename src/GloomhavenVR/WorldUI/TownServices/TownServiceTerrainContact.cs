using System;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Local woodland contacts beneath the shared, fixed resident soles.</summary>
internal sealed class TownServiceTerrainContact : IDisposable
{
    // The original forest is flat only inside 1.7 authored metres. The native town
    // ring reaches its relief: shipping triangles sit 8–58 mm below the fixed soles.
    // Bed only those local contacts, once, rather than publishing another actor Y,
    // sinking feet into the cellar or enlarging the entire forest by almost 3x.
    // Shipping sole-covering triangles reach 0.298 m from the nearest sole.
    // Their three vertices must all be flat; blending one leaves an interpolated gap.
    private const float FlatRadius = .31f, BlendRadius = .45f;
    private readonly MeshFilter _filter;
    private readonly Mesh _original, _owned;
    private bool _disposed;

    private TownServiceTerrainContact(MeshFilter filter, Mesh original, Mesh owned)
    { _filter = filter; _original = original; _owned = owned; }

    internal static TownServiceTerrainContact? Create(GameObject room, bool enabled,
        Vector3 center, Quaternion frame, float scale, float floor)
    {
        if (!enabled || room == null || !(scale > 0f) || float.IsInfinity(scale)
            || float.IsNaN(floor) || float.IsInfinity(floor)) return null;
        MeshFilter? filter = room.transform.Find("RoomGeo/Ground")?.GetComponent<MeshFilter>();
        Mesh? original = filter != null ? filter.sharedMesh : null;
        if (original == null || !original.isReadable) return null;
        var soles = new Vector3[6];
        for (byte service = 1; service <= 3; service++)
        {
            TownServiceLayout.Resolve(TownServiceLayout.Environment.Open, service, 0, out Vector3 offset, out float yaw);
            Quaternion facing = frame * Quaternion.Euler(0f, yaw, 0f);
            Vector3 station = center + frame * offset * scale;
            soles[(service - 1) * 2] = station + facing * new Vector3(-.12f, 0f, .65f) * scale;
            soles[(service - 1) * 2 + 1] = station + facing * new Vector3(.12f, 0f, .65f) * scale;
        }
        Vector3[] vertices = original.vertices;
        var changed = new bool[vertices.Length];
        bool any = false;
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 world = filter!.transform.TransformPoint(vertices[i]);
            float distance = float.PositiveInfinity;
            foreach (Vector3 sole in soles)
                distance = Mathf.Min(distance, new Vector2(world.x - sole.x, world.z - sole.z).magnitude / scale);
            if (distance >= BlendRadius) continue;
            float weight = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FlatRadius, BlendRadius, distance));
            world.y = Mathf.Lerp(world.y, floor, weight);
            Vector3 next = filter.transform.InverseTransformPoint(world);
            if (next == vertices[i]) continue;
            vertices[i] = next; changed[i] = any = true;
        }
        if (!any) return null;
        Mesh owned = UnityEngine.Object.Instantiate(original);
        owned.name = "GloomhavenVR.TownTerrainContact";
        owned.vertices = vertices;
        Vector3[] originalNormals = original.normals;
        owned.RecalculateNormals();
        if (originalNormals.Length == vertices.Length)
        {
            Vector3[] normals = owned.normals;
            for (int i = 0; i < normals.Length; i++) if (!changed[i]) normals[i] = originalNormals[i];
            owned.normals = normals;
        }
        owned.RecalculateBounds();
        filter!.sharedMesh = owned;
        return new TownServiceTerrainContact(filter, original, owned);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_filter != null && _filter.sharedMesh == _owned) _filter.sharedMesh = _original;
        UnityEngine.Object.Destroy(_owned);
    }
}
