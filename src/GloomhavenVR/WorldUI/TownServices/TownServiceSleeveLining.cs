using System;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Shadows the exposed inner cuff of each original resident costume.
/// The source hand-shell removal cut the whole forearm at a common wrist plane,
/// including costume triangles, leaving a visible flat arm end when looking into
/// the sleeve (build558 screenshot085012). This sewn inner lining follows the
/// real forearm and wrist bones. It does not alter hand geometry, silhouette,
/// fingers or the original face/skin atlas.</summary>
internal sealed class TownServiceSleeveLining : IDisposable
{
    private sealed class Side
    {
        internal Transform Forearm = null!, Hand = null!;
        internal Mesh Mesh = null!;
        internal Transform Holder = null!;
        internal Vector3[] Vertices = Array.Empty<Vector3>();
    }
    private const int Segments = 24;
    private readonly Transform _root;
    private readonly Side[] _sides = new Side[2];
    private readonly float _outer;
    private bool _disposed;

    internal TownServiceSleeveLining(Transform station, Transform actor, byte service)
    {
        _root = station;
        // Stay behind the visible cuff. The 20 mm cut also intersects outer
        // garment folds; using its furthest vertex as a radius made a dark
        // flange project outside the original silhouette in close preview.
        _outer = service == 1 ? .044f : .038f;
        SkinnedMeshRenderer? body = null;
        foreach (SkinnedMeshRenderer candidate in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (candidate.name.StartsWith("LOD0_", StringComparison.Ordinal)) { body = candidate; break; }
        if (body == null || body.sharedMaterials.Length == 0 || body.sharedMaterials[0] == null)
            throw new InvalidOperationException("Town sleeve lining requires the original body material.");
        Material material = body.sharedMaterials[0];
        Vector2 swatch = service == 1 ? new Vector2(.40f, .60f)
            : service == 2 ? new Vector2(.40f, .60f) : new Vector2(.25f, .60f);
        for (int side = 0; side < 2; side++)
        {
            string suffix = side == 0 ? ".L" : ".R";
            Transform? forearm = Find(actor, "Forearm" + suffix), hand = Find(actor, "Hand" + suffix);
            if (forearm == null || hand == null)
                throw new InvalidOperationException("Town sleeve lining cannot find the resident wrist bones.");
            var holder = new GameObject("SewnSleeveInterior" + suffix).transform;
            holder.SetParent(actor, false); holder.gameObject.layer = GloomhavenVR.Core.VRLayers.ModLayer;
            Mesh mesh = BuildMesh(swatch);
            holder.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = holder.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material; renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _sides[side] = new Side { Forearm = forearm, Hand = hand, Holder = holder,
                Mesh = mesh, Vertices = new Vector3[Segments * 4 + 1] };
        }
        Tick();
    }

    private static Transform? Find(Transform root, string name)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == name) return child;
        return null;
    }

    private static Mesh BuildMesh(Vector2 swatch)
    {
        var mesh = new Mesh { name = "TownSleeveInterior" };
        var vertices = new Vector3[Segments * 4 + 1];
        var uv = new Vector2[vertices.Length];
        // Atlas swatch from each costume's own dark inner-fabric colour.
        for (int row = 0; row < 4; row++)
            for (int i = 0; i < Segments; i++)
                uv[row * Segments + i] = swatch + new Vector2((i / (float)Segments - .5f) * .0015f, row * .00035f);
        uv[Segments * 4] = swatch;
        var triangles = new int[3 * Segments * 12 + 6 * Segments]; int cursor = 0;
        for (int row = 0; row < 3; row++)
            for (int i = 0; i < Segments; i++)
            {
                int a = row * Segments + i, b = row * Segments + (i + 1) % Segments;
                int c = (row + 1) * Segments + i, d = (row + 1) * Segments + (i + 1) % Segments;
                triangles[cursor++] = a; triangles[cursor++] = b; triangles[cursor++] = c;
                triangles[cursor++] = b; triangles[cursor++] = d; triangles[cursor++] = c;
                triangles[cursor++] = c; triangles[cursor++] = b; triangles[cursor++] = a;
                triangles[cursor++] = c; triangles[cursor++] = d; triangles[cursor++] = b;
            }
        for (int i = 0; i < Segments; i++)
        {
            int a = 3 * Segments + i, b = 3 * Segments + (i + 1) % Segments, c = 4 * Segments;
            triangles[cursor++] = a; triangles[cursor++] = b; triangles[cursor++] = c;
            triangles[cursor++] = c; triangles[cursor++] = b; triangles[cursor++] = a;
        }
        mesh.vertices = vertices; mesh.uv = uv; mesh.triangles = triangles;
        mesh.MarkDynamic(); return mesh;
    }

    internal void Tick()
    {
        if (_disposed) return;
        foreach (Side side in _sides)
        {
            // The forearm joint supplies the stable cloth frame; the hand joint
            // supplies the real wrist centre, so the opening stays aligned as
            // the hand pronates and the elbow reaches across the counter.
            Vector3 centre = side.Hand.position;
            Vector3 axis = (side.Hand.position - side.Forearm.position).normalized;
            if (axis.sqrMagnitude < .9f) continue;
            Vector3 tangent = Vector3.Cross(axis, _root.up);
            if (tangent.sqrMagnitude < .01f) tangent = Vector3.Cross(axis, _root.forward);
            tangent.Normalize(); Vector3 bitangent = Vector3.Cross(axis, tangent).normalized;
            for (int row = 0; row < 4; row++)
            {
                float depth = row == 0 ? -.050f : row == 1 ? -.057f : row == 2 ? -.105f : -.165f;
                float radius = row == 0 ? _outer : row == 1 ? _outer * .72f
                    : row == 2 ? _outer * .73f : _outer * .53f;
                for (int i = 0; i < Segments; i++)
                {
                    float angle = Mathf.PI * 2f * i / Segments;
                    float uneven = 1f + .034f * Mathf.Sin(angle * 5f + row * .7f)
                        + .018f * Mathf.Sin(angle * 9f + row * .4f);
                    Vector3 world = centre + axis * (depth + .002f * Mathf.Sin(angle * 3f + row))
                        + radius * uneven * (tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle));
                    side.Vertices[row * Segments + i] = side.Holder.InverseTransformPoint(world);
                }
            }
            side.Vertices[Segments * 4] = side.Holder.InverseTransformPoint(centre - axis * .205f);
            side.Mesh.vertices = side.Vertices;
            side.Mesh.RecalculateNormals(); side.Mesh.RecalculateBounds();
        }
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        foreach (Side side in _sides)
        {
            if (side == null) continue;
            if (side.Holder != null) UnityEngine.Object.Destroy(side.Holder.gameObject);
            if (side.Mesh != null) UnityEngine.Object.Destroy(side.Mesh);
        }
    }
}
