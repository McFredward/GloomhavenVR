using System;
using System.IO;
using UnityEngine;

namespace GloomhavenVR.Core;

internal static class NativeFigureMeshStream
{
    internal static Mesh Read(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        string format = System.Text.Encoding.ASCII.GetString(reader.ReadBytes(5));
        if (format != "GHFM1" && format != "GHFM2") throw new InvalidDataException("Native mesh format");
        var mesh = new Mesh { name = System.Text.Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32())), indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        Vector3 Vector() => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        Bounds bounds = new() { center = Vector(), extents = Vector() };
        Vector4[] Channel()
        {
            int count = reader.ReadInt32(), width = reader.ReadInt32();
            if (count < 0 || count > 1000000 || width < 2 || width > 4) throw new InvalidDataException("Native vertex channel bound");
            var values = new Vector4[count];
            for (int i = 0; i < count; i++) for (int j = 0; j < width; j++) values[i][j] = reader.ReadSingle();
            return values;
        }
        Vector3[] Vectors(Vector4[] data) { var result = new Vector3[data.Length]; for (int i = 0; i < result.Length; i++) result[i] = data[i]; return result; }
        mesh.vertices = Vectors(Channel()); mesh.normals = Vectors(Channel()); mesh.tangents = Channel();
        Vector4[] colors = Channel(); var color = new Color[colors.Length]; for (int i = 0; i < colors.Length; i++) color[i] = colors[i]; mesh.colors = color;
        for (int index = 0; index < 8; index++) { Vector4[] uv = Channel(); if (uv.Length > 0) mesh.SetUVs(index, new System.Collections.Generic.List<Vector4>(uv)); }
        var weights = new BoneWeight[reader.ReadInt32()];
        for (int i = 0; i < weights.Length; i++)
            weights[i] = new BoneWeight { weight0 = reader.ReadSingle(), weight1 = reader.ReadSingle(), weight2 = reader.ReadSingle(), weight3 = reader.ReadSingle(),
                boneIndex0 = reader.ReadInt32(), boneIndex1 = reader.ReadInt32(), boneIndex2 = reader.ReadInt32(), boneIndex3 = reader.ReadInt32() };
        mesh.boneWeights = weights;
        var poses = new Matrix4x4[reader.ReadInt32()];
        for (int i = 0; i < poses.Length; i++) for (int row = 0; row < 4; row++) for (int column = 0; column < 4; column++) poses[i][row, column] = reader.ReadSingle();
        mesh.bindposes = poses; mesh.subMeshCount = reader.ReadInt32();
        for (int sub = 0; sub < mesh.subMeshCount; sub++)
        { var triangles = new int[reader.ReadInt32()]; for (int i = 0; i < triangles.Length; i++) triangles[i] = reader.ReadInt32(); mesh.SetTriangles(triangles, sub, false); }
        mesh.bounds = bounds;
        if (format == "GHFM2")
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > 128) throw new InvalidDataException("Native expression count bound");
            for (int shape = 0; shape < count; shape++)
            {
                string name = System.Text.Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));
                int frames = reader.ReadInt32();
                if (frames < 1 || frames > 128) throw new InvalidDataException("Native expression frames bound");
                for (int frame = 0; frame < frames; frame++)
                {
                    float weight = reader.ReadSingle(); int sparse = reader.ReadInt32();
                    if (sparse < 0 || sparse > mesh.vertexCount) throw new InvalidDataException("Native expression vertices bound");
                    var dv = new Vector3[mesh.vertexCount]; var dn = new Vector3[mesh.vertexCount]; var dt = new Vector3[mesh.vertexCount];
                    for (int vertex = 0; vertex < sparse; vertex++)
                    { int index = reader.ReadInt32(); dv[index] = Vector(); dn[index] = Vector(); dt[index] = Vector(); }
                    mesh.AddBlendShapeFrame(name, weight, dv, dn, dt);
                }
            }
        }
        if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Native mesh trailing data");
        return mesh;
    }
}
