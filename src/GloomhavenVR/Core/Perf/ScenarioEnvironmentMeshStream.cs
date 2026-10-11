using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR.Core;

internal static class ScenarioEnvironmentMeshStream
{
    // Decode only bounded, SHA-validated private assets. No native readback or online decimation.
    internal static Mesh Read(byte[] bytes)
    {
        using var input = new MemoryStream(bytes, false);
        using var reader = new BinaryReader(input);
        if (Encoding.ASCII.GetString(reader.ReadBytes(5)) != "GHEM1") throw new InvalidDataException("environment geometry format");
        int nameLength = reader.ReadInt32();
        if (nameLength < 0 || nameLength > 1024) throw new InvalidDataException("environment name bound");
        string name = Encoding.UTF8.GetString(reader.ReadBytes(nameLength));
        float Finite() { float value = reader.ReadSingle(); if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidDataException("non-finite geometry"); return value; }
        Vector3 Vector() => new(Finite(), Finite(), Finite());
        Bounds bounds = new() { center = Vector(), extents = Vector() };
        if (bounds.extents.x < 0 || bounds.extents.y < 0 || bounds.extents.z < 0) throw new InvalidDataException("environment bounds");
        int vertices = -1;
        Vector4[] Channel(out int width)
        {
            int count = reader.ReadInt32(); width = reader.ReadInt32();
            if (count < 0 || count > 100000 || width < 2 || width > 4 || (vertices >= 0 && count != 0 && count != vertices))
                throw new InvalidDataException("environment vertex channel bound");
            var result = new Vector4[count];
            for (int i = 0; i < count; i++) for (int j = 0; j < width; j++) result[i][j] = Finite();
            return result;
        }
        Vector3[] Vectors(Vector4[] data) { var result = new Vector3[data.Length]; for (int i = 0; i < data.Length; i++) result[i] = data[i]; return result; }
        Mesh? mesh = null;
        try
        {
            mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            Vector4[] positions = Channel(out int positionWidth); vertices = positions.Length;
            if (vertices < 3 || positionWidth != 3) throw new InvalidDataException("environment positions");
            mesh.vertices = Vectors(positions); mesh.normals = Vectors(Channel(out _)); mesh.tangents = Channel(out _);
            Vector4[] values = Channel(out _); var colors = new Color[values.Length]; for (int i = 0; i < values.Length; i++) colors[i] = values[i]; mesh.colors = colors;
            for (int i = 0; i < 8; i++)
            {
                Vector4[] uv = Channel(out int width);
                if (uv.Length == 0) continue;
                if (width == 2) { var data = new List<Vector2>(uv.Length); foreach (Vector4 value in uv) data.Add(value); mesh.SetUVs(i, data); }
                else if (width == 3) mesh.SetUVs(i, new List<Vector3>(Vectors(uv)));
                else mesh.SetUVs(i, new List<Vector4>(uv));
            }
            int subs = reader.ReadInt32();
            if (subs < 1 || subs > 32) throw new InvalidDataException("environment submesh bound");
            mesh.subMeshCount = subs;
            for (int sub = 0; sub < subs; sub++)
            {
                int count = reader.ReadInt32();
                if (count < 0 || count > 3000000 || count % 3 != 0) throw new InvalidDataException("environment index bound");
                var indices = new int[count];
                for (int i = 0; i < count; i++) { indices[i] = reader.ReadInt32(); if (indices[i] < 0 || indices[i] >= vertices) throw new InvalidDataException("environment index range"); }
                mesh.SetTriangles(indices, sub, false);
            }
            if (input.Position != input.Length) throw new InvalidDataException("environment trailing data");
            mesh.bounds = bounds;
            return mesh;
        }
        catch { if (mesh != null) UnityEngine.Object.Destroy(mesh); throw; }
    }
}
