using System;
using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>Offline endpoint edge-collapse simplifier for original native actor surfaces.
/// Each survivor is an ORIGINAL vertex: every UV, normal, tangent, color, bone influence
/// and blend-shape delta is copied exactly. Manifold link checks, pinned open boundaries,
/// coincident seam vertices, material junctions, skin-weight discontinuities and a face
/// flip/error bound constrain collapse. No runtime triangle deletion or remeshing occurs.
/// The requested fraction is a target, never permission to break a constrained surface.</summary>
internal static class ScenarioFigureMeshTopology
{
    private sealed class Vertex
    {
        internal readonly HashSet<int> Faces = new();
        internal readonly HashSet<int> Neighbors = new();
        internal readonly double[] Q = new double[10];
        internal bool Pinned, Removed;
    }
    private sealed class Face
    {
        internal int A, B, C, Material;
        internal bool Removed;
        internal Vector3 Normal;
        internal bool Contains(int index) => A == index || B == index || C == index;
    }
    private readonly struct Edge
    {
        internal Edge(int a, int b, double cost) { A = a; B = b; Cost = cost; }
        internal readonly int A, B;
        internal readonly double Cost;
    }
    private static ulong EdgeKey(int a, int b) => ((ulong)(uint)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);

    internal static Mesh Simplify(Mesh source, float fraction, out int[] originalVertices)
    {
        if (!source.isReadable) throw new ArgumentException("Offline source must be readable");
        Vector3[] positions = source.vertices;
        var vertices = new Vertex[positions.Length];
        for (int i = 0; i < vertices.Length; i++) vertices[i] = new Vertex();
        var faces = new List<Face>();
        var edges = new Dictionary<ulong, List<int>>();
        var coincident = new Dictionary<Vector3, int>();
        BoneWeight[] weights = source.boneWeights;
        var materials = new int[positions.Length];
        for (int i = 0; i < materials.Length; i++) materials[i] = -1;
        for (int i = 0; i < positions.Length; i++)
        {
            if (coincident.TryGetValue(positions[i], out int prior))
            { vertices[i].Pinned = true; vertices[prior].Pinned = true; }
            else coincident.Add(positions[i], i);
        }
        for (int sub = 0; sub < source.subMeshCount; sub++)
        {
            if (source.GetTopology(sub) != UnityEngine.MeshTopology.Triangles)
                throw new ArgumentException("Only native triangle surfaces can be simplified");
            int[] triangles = source.GetTriangles(sub);
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                Vector3 normal = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
                // Some originals contain intentionally collapsed/zero-area seam triangles.
                // Retain them exactly and pin their endpoints, rather than dropping a source
                // primitive or rejecting an otherwise valid complete actor.
                if (a == b || b == c || c == a || normal.sqrMagnitude < 1e-20f)
                { vertices[a].Pinned = true; vertices[b].Pinned = true; vertices[c].Pinned = true; }
                normal.Normalize();
                int index = faces.Count;
                faces.Add(new Face { A = a, B = b, C = c, Material = sub, Normal = normal });
                foreach (int vertex in new[] { a, b, c })
                {
                    vertices[vertex].Faces.Add(index);
                    if (materials[vertex] >= 0 && materials[vertex] != sub) vertices[vertex].Pinned = true;
                    else materials[vertex] = sub;
                    AddPlane(vertices[vertex].Q, normal, -Vector3.Dot(normal, positions[a]));
                }
                AddNeighbors(vertices, a, b); AddNeighbors(vertices, b, c); AddNeighbors(vertices, c, a);
                foreach (ulong key in new[] { EdgeKey(a, b), EdgeKey(b, c), EdgeKey(c, a) })
                {
                    if (!edges.TryGetValue(key, out List<int> edgeFaces)) edges.Add(key, edgeFaces = new());
                    edgeFaces.Add(index);
                }
            }
        }
        foreach (var edge in edges)
            if (edge.Value.Count != 2)
            { vertices[(int)(edge.Key >> 32)].Pinned = true; vertices[(int)(edge.Key & uint.MaxValue)].Pinned = true; }
        int remaining = faces.Count, target = Math.Max(source.subMeshCount * 4, (int)Math.Ceiling(remaining * fraction));
        double errorLimit = source.bounds.size.sqrMagnitude * .0009; // <3% plane-distance envelope.
        for (int pass = 0; pass < 32 && remaining > target; pass++)
        {
            var candidates = new List<Edge>();
            for (int a = 0; a < vertices.Length; a++)
            {
                if (vertices[a].Pinned || vertices[a].Removed) continue;
                foreach (int b in vertices[a].Neighbors)
                {
                    if (b <= a || vertices[b].Removed || vertices[b].Pinned) continue;
                    double ab = Cost(vertices[a].Q, vertices[b].Q, positions[a]);
                    double ba = Cost(vertices[a].Q, vertices[b].Q, positions[b]);
                    double cost = Math.Min(ab, ba) + (positions[a] - positions[b]).sqrMagnitude * .0001;
                    candidates.Add(ab <= ba ? new Edge(a, b, cost) : new Edge(b, a, cost));
                }
            }
            candidates.Sort((a, b) => { int c = a.Cost.CompareTo(b.Cost); return c != 0 ? c : a.A != b.A ? a.A.CompareTo(b.A) : a.B.CompareTo(b.B); });
            int collapsed = 0;
            foreach (Edge edge in candidates)
            {
                if (remaining <= target) break;
                int keep = edge.A, remove = edge.B;
                if (vertices[keep].Removed || vertices[remove].Removed || vertices[keep].Pinned || vertices[remove].Pinned
                    || !vertices[keep].Neighbors.Contains(remove)) continue;
                if (Cost(vertices[keep].Q, vertices[remove].Q, positions[keep]) > errorLimit
                    || (weights.Length == vertices.Length && WeightDistance(weights[keep], weights[remove]) > .45f)) continue;
                if (!CanCollapse(vertices, faces, positions, keep, remove)) continue;
                var affected = new List<int>(vertices[remove].Faces);
                var neighborRefresh = new HashSet<int>(vertices[remove].Neighbors) { keep };
                foreach (int index in affected)
                {
                    Face face = faces[index];
                    if (face.Removed) continue;
                    if (face.Contains(keep))
                    {
                        face.Removed = true; remaining--;
                        vertices[face.A].Faces.Remove(index); vertices[face.B].Faces.Remove(index); vertices[face.C].Faces.Remove(index);
                    }
                    else
                    {
                        if (face.A == remove) face.A = keep;
                        if (face.B == remove) face.B = keep;
                        if (face.C == remove) face.C = keep;
                        vertices[keep].Faces.Add(index);
                    }
                }
                vertices[remove].Removed = true; vertices[remove].Faces.Clear(); vertices[remove].Neighbors.Clear();
                for (int q = 0; q < 10; q++) vertices[keep].Q[q] += vertices[remove].Q[q];
                foreach (int index in neighborRefresh) RebuildNeighbors(vertices, faces, index);
                collapsed++;
            }
            if (collapsed == 0) break;
        }
        var used = new bool[vertices.Length];
        foreach (Face face in faces) if (!face.Removed) { used[face.A] = true; used[face.B] = true; used[face.C] = true; }
        var survivors = new List<int>(); var remap = new int[vertices.Length];
        for (int i = 0; i < vertices.Length; i++) if (used[i]) { remap[i] = survivors.Count; survivors.Add(i); }
        originalVertices = survivors.ToArray();
        var result = new Mesh { name = source.name + " derived", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        result.vertices = Copy(positions, originalVertices);
        result.normals = Copy(source.normals, originalVertices); result.tangents = Copy(source.tangents, originalVertices);
        result.colors = Copy(source.colors, originalVertices);
        for (int channel = 0; channel < 8; channel++)
        { var uv = new List<Vector4>(); source.GetUVs(channel, uv); if (uv.Count > 0) result.SetUVs(channel, new List<Vector4>(Copy(uv.ToArray(), originalVertices))); }
        result.boneWeights = Copy(weights, originalVertices); result.bindposes = source.bindposes;
        result.subMeshCount = source.subMeshCount;
        for (int sub = 0; sub < result.subMeshCount; sub++)
        {
            var indices = new List<int>();
            foreach (Face face in faces) if (!face.Removed && face.Material == sub)
            { indices.Add(remap[face.A]); indices.Add(remap[face.B]); indices.Add(remap[face.C]); }
            result.SetTriangles(indices, sub, false);
        }
        result.bounds = source.bounds; // Original conservative animation/culling envelope.
        for (int shape = 0; shape < source.blendShapeCount; shape++)
            for (int frame = 0; frame < source.GetBlendShapeFrameCount(shape); frame++)
            {
                var dv = new Vector3[positions.Length]; var dn = new Vector3[positions.Length]; var dt = new Vector3[positions.Length];
                source.GetBlendShapeFrameVertices(shape, frame, dv, dn, dt);
                result.AddBlendShapeFrame(source.GetBlendShapeName(shape), source.GetBlendShapeFrameWeight(shape, frame),
                    Copy(dv, originalVertices), Copy(dn, originalVertices), Copy(dt, originalVertices));
            }
        return result;
    }
    private static T[] Copy<T>(T[] data, int[] survivors)
    {
        if (data.Length == 0) return Array.Empty<T>();
        var result = new T[survivors.Length]; for (int i = 0; i < result.Length; i++) result[i] = data[survivors[i]]; return result;
    }
    private static void AddNeighbors(Vertex[] vertices, int a, int b)
    { vertices[a].Neighbors.Add(b); vertices[b].Neighbors.Add(a); }
    private static void RebuildNeighbors(Vertex[] vertices, List<Face> faces, int vertex)
    {
        Vertex item = vertices[vertex]; item.Neighbors.Clear();
        foreach (int index in item.Faces)
        { Face face = faces[index]; if (face.Removed) continue; if (face.A != vertex) item.Neighbors.Add(face.A); if (face.B != vertex) item.Neighbors.Add(face.B); if (face.C != vertex) item.Neighbors.Add(face.C); }
    }
    private static bool CanCollapse(Vertex[] vertices, List<Face> faces, Vector3[] positions, int keep, int remove)
    {
        int shared = 0, common = 0;
        foreach (int index in vertices[remove].Faces) if (!faces[index].Removed && faces[index].Contains(keep)) shared++;
        foreach (int neighbor in vertices[keep].Neighbors) if (vertices[remove].Neighbors.Contains(neighbor)) common++;
        if (shared != 2 || common != 2) return false; // Manifold link condition: no new holes/handles.
        foreach (int index in vertices[remove].Faces)
        {
            Face face = faces[index]; if (face.Removed || face.Contains(keep)) continue;
            int ia = face.A == remove ? keep : face.A, ib = face.B == remove ? keep : face.B, ic = face.C == remove ? keep : face.C;
            // The neighbor-only link test is insufficient for a tetrahedral terminal shell:
            // collapsing it would produce duplicate opposite faces and lose the entire volume.
            // Preserve those small closed components and every nonshared face's identity.
            foreach (int existing in vertices[keep].Faces)
            {
                Face other = faces[existing];
                if (!other.Removed && !other.Contains(remove) && other.Contains(ia) && other.Contains(ib) && other.Contains(ic))
                    return false;
            }
            Vector3 a = positions[ia], b = positions[ib], c = positions[ic];
            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude < 1e-20f || Vector3.Dot(normal.normalized, face.Normal) < .3f) return false;
        }
        return true;
    }
    private static float WeightDistance(BoneWeight a, BoneWeight b)
    {
        float distance = 0;
        var bones = new HashSet<int> { a.boneIndex0, a.boneIndex1, a.boneIndex2, a.boneIndex3, b.boneIndex0, b.boneIndex1, b.boneIndex2, b.boneIndex3 };
        foreach (int bone in bones) distance += Mathf.Abs(Weight(a, bone) - Weight(b, bone));
        return distance;
    }
    private static float Weight(BoneWeight a, int bone) => (a.boneIndex0 == bone ? a.weight0 : 0) + (a.boneIndex1 == bone ? a.weight1 : 0)
        + (a.boneIndex2 == bone ? a.weight2 : 0) + (a.boneIndex3 == bone ? a.weight3 : 0);
    private static void AddPlane(double[] q, Vector3 n, float d)
    { double x = n.x, y = n.y, z = n.z; q[0] += x*x; q[1] += x*y; q[2] += x*z; q[3] += x*d; q[4] += y*y; q[5] += y*z; q[6] += y*d; q[7] += z*z; q[8] += z*d; q[9] += d*d; }
    private static double Cost(double[] a, double[] b, Vector3 p)
    {
        double x = p.x, y = p.y, z = p.z;
        return Math.Max(0, (a[0]+b[0])*x*x + 2*(a[1]+b[1])*x*y + 2*(a[2]+b[2])*x*z + 2*(a[3]+b[3])*x
            + (a[4]+b[4])*y*y + 2*(a[5]+b[5])*y*z + 2*(a[6]+b[6])*y + (a[7]+b[7])*z*z + 2*(a[8]+b[8])*z + a[9]+b[9]);
    }
}
