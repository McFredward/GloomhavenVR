using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR.Core;

/// <summary>Own the unlit vertex inputs of Quest's small runtime primitives.</summary>
internal static class QuestPrimitiveMesh
{
    private static int _diagnostics;

    // The caller owns the returned upload and releases it with its visual.
    // A refusal leaves the original primitive intact, including desktop identity.
    internal static Mesh? Rebuild(Mesh? source, string purpose)
    {
        if (!QuestStandalonePlatform.Enabled || source == null) return null;
        Mesh? copy = null;
        try
        {
            copy = CreateUnlitCopy(source);
            if (VRLog.Wants(VRLogLevel.Debug) && _diagnostics < 8)
            {
                _diagnostics++;
                UnityEngine.Debug.Log("[Quest startup] presentation primitive-upload purpose=" + purpose
                    + " status=owned source=" + Describe(source) + " owned=" + Describe(copy));
            }
            return copy;
        }
        catch (Exception error)
        {
            if (copy != null) UnityEngine.Object.Destroy(copy);
            if (VRLog.Wants(VRLogLevel.Debug) && _diagnostics < 8)
            {
                _diagnostics++;
                UnityEngine.Debug.Log("[Quest startup] presentation primitive-upload purpose=" + purpose
                    + " status=native-retained error=" + error.GetType().Name);
            }
            return null;
        }
    }

    /// <summary>
    /// Re-upload the actual CPU geometry, preserving all inputs consumed by the
    /// unlit screen/pointer programs. B676 still shows blue facets and a black
    /// menu with the owned shader and neutral Sprite state. Both failing objects
    /// are builtin MeshRenderers without COLOR; healthy hands use authored
    /// streams. CPU bounds do not observe GPU vertex fetch. An explicit white
    /// COLOR and fresh position/UV/index buffers remove that shared dependency,
    /// without changing geometry, shader, eye routing, transforms or material.
    /// This is a controlled primitive-input repair, not proof of the exact
    /// Android driver defect. Desktop keeps its original mesh by identity.
    /// </summary>
    private static Mesh CreateUnlitCopy(Mesh source)
    {
        if (source == null || !source.isReadable || source.vertexCount == 0 || source.vertexCount > 65535)
            throw new InvalidOperationException("Quest unlit primitive requires bounded readable CPU geometry.");
        Vector3[] positions = source.vertices;
        Vector2[] uv = source.uv;
        Color[] colors = source.colors;
        if (uv.Length != positions.Length || (colors.Length != 0 && colors.Length != positions.Length))
            throw new InvalidOperationException("Quest unlit primitive has incomplete vertex inputs.");
        if (colors.Length == 0)
        {
            colors = new Color[positions.Length];
            for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
        }
        for (int i = 0; i < positions.Length; i++)
            if (!Finite(positions[i].x) || !Finite(positions[i].y) || !Finite(positions[i].z)
                || !Finite(uv[i].x) || !Finite(uv[i].y)
                || !Finite(colors[i].r) || !Finite(colors[i].g) || !Finite(colors[i].b) || !Finite(colors[i].a))
                throw new InvalidOperationException("Quest unlit primitive has non-finite CPU vertex inputs.");
        if (source.subMeshCount < 1 || source.subMeshCount > 8)
            throw new InvalidOperationException("Quest unlit primitive has excessive submeshes.");
        var indices = new int[source.subMeshCount][];
        int totalIndices = 0;
        for (int submesh = 0; submesh < indices.Length; submesh++)
        {
            if (source.GetTopology(submesh) != MeshTopology.Triangles)
                throw new InvalidOperationException("Quest unlit primitive requires triangle topology.");
            uint count = source.GetIndexCount(submesh);
            if (count > 1024 * 1024 || totalIndices > 1024 * 1024 - count)
                throw new InvalidOperationException("Quest unlit primitive has excessive indices.");
            totalIndices += (int)count;
            indices[submesh] = source.GetIndices(submesh, true);
            if (indices[submesh].Length % 3 != 0)
                throw new InvalidOperationException("Quest unlit primitive has incomplete triangles.");
            foreach (int index in indices[submesh])
                if (index < 0 || index >= positions.Length)
                    throw new InvalidOperationException("Quest unlit primitive has an invalid CPU index.");
        }
        Bounds bounds = source.bounds;
        if (!Finite(bounds.center.x) || !Finite(bounds.center.y) || !Finite(bounds.center.z)
            || !Finite(bounds.size.x) || !Finite(bounds.size.y) || !Finite(bounds.size.z))
            throw new InvalidOperationException("Quest unlit primitive has non-finite bounds.");
        var result = new Mesh { name = "GloomhavenVR.QuestPrimitive." + source.name, indexFormat = source.indexFormat };
        try
        {
            result.vertices = positions;
            result.uv = uv;
            result.colors = colors;
            result.subMeshCount = indices.Length;
            for (int submesh = 0; submesh < indices.Length; submesh++)
                result.SetIndices(indices[submesh], MeshTopology.Triangles, submesh, false);
            result.bounds = bounds;
            result.UploadMeshData(false);
            return result;
        }
        catch
        {
            UnityEngine.Object.Destroy(result);
            throw;
        }
    }

    internal static string Describe(Mesh mesh)
    {
        Vector3[] vertices = mesh.vertices;
        Vector2[] uv = mesh.uv;
        Vector3 minimum = Vector3.positiveInfinity, maximum = Vector3.negativeInfinity;
        Vector2 uvMinimum = Vector2.positiveInfinity, uvMaximum = Vector2.negativeInfinity;
        bool finite = true;
        foreach (Vector3 point in vertices)
        {
            finite &= Finite(point.x) && Finite(point.y) && Finite(point.z);
            minimum = Vector3.Min(minimum, point); maximum = Vector3.Max(maximum, point);
        }
        foreach (Vector2 point in uv)
        {
            finite &= Finite(point.x) && Finite(point.y);
            uvMinimum = Vector2.Min(uvMinimum, point); uvMaximum = Vector2.Max(uvMaximum, point);
        }
        int indexCount = 0, minimumIndex = int.MaxValue, maximumIndex = -1;
        for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            foreach (int index in mesh.GetIndices(submesh, true))
            {
                indexCount++; minimumIndex = Math.Min(minimumIndex, index); maximumIndex = Math.Max(maximumIndex, index);
            }
        return "mesh=" + mesh.name + " vertices=" + vertices.Length + " stride=" + mesh.GetVertexBufferStride(0)
            + " color=" + mesh.HasVertexAttribute(VertexAttribute.Color) + " finite=" + finite
            + " positionMin=" + Vector(minimum) + " positionMax=" + Vector(maximum)
            + " uvMin=" + Vector(uvMinimum) + " uvMax=" + Vector(uvMaximum)
            + " indices=" + indexCount + " indexRange=" + minimumIndex + ":" + maximumIndex;
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static string Vector(Vector3 value) => "(" + value.x.ToString("G6", CultureInfo.InvariantCulture) + ","
        + value.y.ToString("G6", CultureInfo.InvariantCulture) + "," + value.z.ToString("G6", CultureInfo.InvariantCulture) + ")";
}
