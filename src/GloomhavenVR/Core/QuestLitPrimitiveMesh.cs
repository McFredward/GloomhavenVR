using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR.Core;

/// <summary>Own the small lit Cube inputs without discarding authored lighting data.</summary>
internal static class QuestLitPrimitiveMesh
{
    private static int _diagnostics;

    // The successful B677 Quad/Sphere upload must not be reused for lit piles:
    // that helper deliberately strips normals/tangents. Here retain every native
    // primitive attribute and submesh, adding only an explicit default white COLOR.
    // Geometry, Standard/emission material, depth, placement and desktop identity
    // stay unchanged. Callers own the returned mesh; this is not a frame watcher.
    internal static Mesh? Rebuild(Mesh? source, string purpose)
    {
        if (!QuestStandalonePlatform.Enabled || source == null) return null;
        Mesh? result = null;
        try
        {
            if (!source.isReadable || source.vertexCount < 1 || source.vertexCount > 1024
                || source.subMeshCount < 1 || source.subMeshCount > 8)
                throw new InvalidOperationException("Lit primitive requires small readable geometry.");
            foreach (VertexAttributeDescriptor attribute in source.GetVertexAttributes())
                if (attribute.attribute != VertexAttribute.Position && attribute.attribute != VertexAttribute.Normal
                    && attribute.attribute != VertexAttribute.Tangent && attribute.attribute != VertexAttribute.Color
                    && (attribute.attribute < VertexAttribute.TexCoord0 || attribute.attribute > VertexAttribute.TexCoord7))
                    throw new InvalidOperationException("Unexpected non-primitive vertex input.");
            var positions = source.vertices;
            var normals = source.normals;
            var tangents = source.tangents;
            var colors = source.colors;
            if (normals.Length != positions.Length || (tangents.Length != 0 && tangents.Length != positions.Length)
                || (colors.Length != 0 && colors.Length != positions.Length))
                throw new InvalidOperationException("Lit primitive has incomplete vertex inputs.");
            if (colors.Length == 0)
            {
                colors = new Color[positions.Length];
                for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
            }
            foreach (Vector3 position in positions) RequireFinite(position);
            foreach (Vector3 normal in normals) RequireFinite(normal);
            foreach (Vector4 tangent in tangents) RequireFinite(tangent);
            foreach (Color color in colors) RequireFinite(new Vector4(color.r, color.g, color.b, color.a));
            result = new Mesh { name = "GloomhavenVR.QuestLitPrimitive." + source.name, indexFormat = source.indexFormat };
            result.vertices = positions; result.normals = normals;
            if (tangents.Length != 0) result.tangents = tangents;
            result.colors = colors;
            for (int channel = 0; channel < 8; channel++)
            {
                var attribute = (VertexAttribute)((int)VertexAttribute.TexCoord0 + channel);
                if (!source.HasVertexAttribute(attribute)) continue;
                int dimension = source.GetVertexAttributeDimension(attribute);
                var values = new List<Vector4>(); source.GetUVs(channel, values);
                if (values.Count != positions.Length) throw new InvalidOperationException("Incomplete primitive UV stream.");
                foreach (Vector4 value in values) RequireFinite(value);
                if (dimension == 2) result.SetUVs(channel, values.ConvertAll(v => new Vector2(v.x, v.y)));
                else if (dimension == 3) result.SetUVs(channel, values.ConvertAll(v => new Vector3(v.x, v.y, v.z)));
                else if (dimension == 4) result.SetUVs(channel, values);
                else throw new InvalidOperationException("Unsupported primitive UV dimension.");
            }
            result.subMeshCount = source.subMeshCount;
            int count = 0;
            for (int submesh = 0; submesh < source.subMeshCount; submesh++)
            {
                if (source.GetTopology(submesh) != MeshTopology.Triangles || source.GetIndexCount(submesh) > 65536)
                    throw new InvalidOperationException("Lit primitive requires bounded triangle submeshes.");
                int[] indices = source.GetIndices(submesh, true); count += indices.Length;
                if (indices.Length % 3 != 0 || count > 65536) throw new InvalidOperationException("Invalid primitive index count.");
                foreach (int index in indices)
                    if (index < 0 || index >= positions.Length) throw new InvalidOperationException("Invalid primitive index.");
                result.SetIndices(indices, MeshTopology.Triangles, submesh, false);
            }
            RequireFinite(source.bounds.center); RequireFinite(source.bounds.size);
            result.bounds = source.bounds; result.UploadMeshData(false);
            Report("purpose=" + purpose + " status=owned vertices=" + result.vertexCount
                + " sourceStride=" + source.GetVertexBufferStride(0) + " ownedStride=" + result.GetVertexBufferStride(0)
                + " normals=" + result.normals.Length + " tangents=" + result.tangents.Length);
            return result;
        }
        catch (Exception error)
        {
            if (result != null) UnityEngine.Object.Destroy(result);
            Report("purpose=" + purpose + " status=native-retained error=" + error.GetType().Name);
            return null;
        }
    }

    private static void RequireFinite(Vector4 value)
    {
        if (!Finite(value.x) || !Finite(value.y) || !Finite(value.z) || !Finite(value.w))
            throw new InvalidOperationException("Non-finite primitive input.");
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static void Report(string message)
    {
        if (!VRLog.WantsDebug || _diagnostics >= 4) return;
        _diagnostics++;
        UnityEngine.Debug.Log("[Quest startup] presentation primitive-upload lit=True " + message);
    }
}
