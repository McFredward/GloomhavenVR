using System;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Core;
using UnityEditor;
using UnityEngine;

/// <summary>Append-only far/NPC bank generation. Existing figure banks remain byte-for-byte
/// unchanged. Far surfaces allow a larger geometric error and interpolate body attributes, with
/// fixed open boundaries and native material slots. Original NPC facial triangles remain untouched.</summary>
public static class FigureDistanceMeshGenerator
{
    private static readonly List<AssetBundleBuild> Banks = new();
    private static readonly List<string> Index = new(), Report = new();
    private static readonly List<string> Part = new();
    private static long _bytes;
    private static int _number;
    private static string _output = "";
    public static void Build()
    {
        string[] args = Environment.GetCommandLineArgs();
        string Arg(string key) => args[Array.IndexOf(args, key) + 1];
        string input = Arg("-nativeMeshInput"); _output = Arg("-nativeMeshOutput");
        Directory.CreateDirectory(_output);
        AssetDatabase.DeleteAsset("Assets/DistanceGenerated"); Directory.CreateDirectory("Assets/DistanceGenerated");
        Sources sources = JsonUtility.FromJson<Sources>(File.ReadAllText(Path.Combine(input, "sources.json")));
        foreach (Source source in sources.meshes)
        {
            if (Array.IndexOf(args, "-onlyNpcMeshes") >= 0 && !source.npc) continue;
            Mesh original = NativeFigureMeshStream.Read(Path.Combine(input, source.file));
            string key = ScenarioFigureMeshBank.Key(original, source.readable);
            foreach (int tier in source.npc ? new[] { 75, 45, 20, 5 } : new[] { 5 })
                Add(original, key, tier, source.npc ? source.protectedBones : null);
            UnityEngine.Object.DestroyImmediate(original);
        }
        Flush(); AssetDatabase.SaveAssets();
        BuildPipeline.BuildAssetBundles(_output, Banks.ToArray(), BuildAssetBundleOptions.ChunkBasedCompression
            | BuildAssetBundleOptions.DeterministicAssetBundle, BuildTarget.StandaloneWindows64);
        File.WriteAllText(Path.Combine(_output, "index.json"), "{\"entries\":[" + string.Join(",\n", Index) + "]}\n");
        File.WriteAllText(Path.Combine(_output, "results.json"), "[" + string.Join(",\n", Report) + "]\n");
        Debug.Log("Distance mesh derivatives=" + Report.Count + " parts=" + Banks.Count);
        EditorApplication.Exit(0);
    }
    private static void Add(Mesh original, string key, int tier, int[]? protect)
    {
        UnityMeshSimplifier.MeshSimplifier simplifier;
        var options = UnityMeshSimplifier.SimplificationOptions.Default;
        options.EnableSmartLink = true;
        options.PreserveBorderEdges = true;
        options.PreserveUVSeamEdges = false;
        options.PreserveUVFoldoverEdges = false;

        bool[]? locked = null;
        if (protect != null)
        {
            var indices = new HashSet<int>(protect); BoneWeight[] weights = original.boneWeights;
            locked = new bool[original.vertexCount];
            Vector3[] positions = original.vertices;
            var dv = new Vector3[positions.Length]; var dn = new Vector3[positions.Length]; var dt = new Vector3[positions.Length];
            Bounds expressionBounds = default; bool foundExpression = false;
            for (int shape = 0; shape < original.blendShapeCount; shape++)
                for (int frame = 0; frame < original.GetBlendShapeFrameCount(shape); frame++)
                {
                    original.GetBlendShapeFrameVertices(shape, frame, dv, dn, dt);
                    for (int i = 0; i < positions.Length; i++)
                        if (dv[i].sqrMagnitude + dn[i].sqrMagnitude + dt[i].sqrMagnitude > 1e-16f)
                        {
                            locked[i] = true;
                            if (foundExpression) expressionBounds.Encapsulate(positions[i]);
                            else { expressionBounds = new Bounds(positions[i], Vector3.zero); foundExpression = true; }
                        }
                }
            if (!foundExpression) throw new InvalidDataException("Native NPC facial expression envelope unavailable");
            expressionBounds.Expand(expressionBounds.size * .1f);
            for (int i = 0; i < weights.Length; i++)
            {
                BoneWeight w = weights[i];
                locked[i] |= expressionBounds.Contains(positions[i]) && ((w.weight0 > .05f && indices.Contains(w.boneIndex0))
                    || (w.weight1 > .05f && indices.Contains(w.boneIndex1))
                    || (w.weight2 > .05f && indices.Contains(w.boneIndex2))
                    || (w.weight3 > .05f && indices.Contains(w.boneIndex3)));
            }

        }
        Mesh derived;
        if (locked != null) derived = FacialHybrid(original, locked, tier / 100f, options);
        else
        {
            simplifier = new UnityMeshSimplifier.MeshSimplifier(original);
            simplifier.SimplificationOptions = options;
            simplifier.SimplifyMesh(tier / 100f);
            derived = simplifier.ToMesh();
        }
        derived.bounds = original.bounds;
        Validate(original, derived, locked);
        int originalTriangles = Triangles(original), triangles = Triangles(derived);
        if (triangles >= originalTriangles * .97f || derived.vertexCount >= original.vertexCount)
        { UnityEngine.Object.DestroyImmediate(derived); return; }
        derived.name = key + "-" + tier;
        string path = "Assets/DistanceGenerated/" + derived.name + ".asset";
        AssetDatabase.CreateAsset(derived, path);
        long bytes = (long)derived.vertexCount * (128 + original.blendShapeCount * 36) + (long)triangles * 12;
        if (Part.Count >= 24 || _bytes + bytes > 16 * 1024 * 1024) Flush();
        Part.Add(path); _bytes += bytes;
        // SmartLink can interpolate body vertices; a survivor map would be misleading.
        // Exact protected facial attributes and original expression names are verified below.
        Report.Add(JsonUtility.ToJson(new Result { source = original.name, key = key, tier = tier,
            sourceVertices = original.vertexCount, vertices = derived.vertexCount, sourceTriangles = originalTriangles,
            triangles = triangles, blendShapes = original.blendShapeCount, protectedBones = protect?.Length ?? 0 }));
        Debug.Log("Distance mesh " + key + " tier=" + tier + " triangles=" + originalTriangles + "->" + triangles);
    }
    private static Mesh FacialHybrid(Mesh original, bool[] protectedVertices, float quality,
        UnityMeshSimplifier.SimplificationOptions options)
    {
        Mesh body = UnityEngine.Object.Instantiate(original);
        var faces = new List<int>[original.subMeshCount]; var faceVertices = new HashSet<int>();
        for (int sub = 0; sub < original.subMeshCount; sub++)
        {
            faces[sub] = new List<int>(); var bodyIndices = new List<int>(); int[] triangles = original.GetTriangles(sub);
            for (int t = 0; t < triangles.Length; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                if (protectedVertices[a] || protectedVertices[b] || protectedVertices[c])
                { faces[sub].AddRange(new[] { a, b, c }); faceVertices.Add(a); faceVertices.Add(b); faceVertices.Add(c); }
                else bodyIndices.AddRange(new[] { a, b, c });
            }
            body.SetTriangles(bodyIndices, sub, false);
        }
        // The open cut remains fixed: simplified body triangles meet the untouched face
        // at original positions, without UV/weight interpolation on a facial vertex.
        var simplifier = new UnityMeshSimplifier.MeshSimplifier(body); simplifier.SimplificationOptions = options;
        simplifier.SimplifyMesh(quality); Mesh reduced = simplifier.ToMesh();
        var retained = new List<int>(faceVertices); retained.Sort(); int offset = reduced.vertexCount;
        var remap = new Dictionary<int, int>(); for (int i = 0; i < retained.Count; i++) remap.Add(retained[i], offset + i);
        T[] Join<T>(T[] coarse, T[] source)
        {
            if (source.Length == 0) return Array.Empty<T>();
            var result = new T[offset + retained.Count]; Array.Copy(coarse, result, offset);
            for (int i = 0; i < retained.Count; i++) result[offset + i] = source[retained[i]];
            return result;
        }
        var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.vertices = Join(reduced.vertices, original.vertices); mesh.normals = Join(reduced.normals, original.normals);
        mesh.tangents = Join(reduced.tangents, original.tangents); mesh.colors = Join(reduced.colors, original.colors);
        mesh.boneWeights = Join(reduced.boneWeights, original.boneWeights); mesh.bindposes = original.bindposes;
        for (int channel = 0; channel < 8; channel++)
        {
            var sourceUv = new List<Vector4>(); var bodyUv = new List<Vector4>();
            original.GetUVs(channel, sourceUv); reduced.GetUVs(channel, bodyUv);
            if (sourceUv.Count > 0) mesh.SetUVs(channel, new List<Vector4>(Join(bodyUv.ToArray(), sourceUv.ToArray())));
        }
        mesh.subMeshCount = original.subMeshCount;
        for (int sub = 0; sub < mesh.subMeshCount; sub++)
        {
            var indices = new List<int>(reduced.GetTriangles(sub));
            foreach (int originalIndex in faces[sub]) indices.Add(remap[originalIndex]);
            mesh.SetTriangles(indices, sub, false);
        }
        for (int shape = 0; shape < original.blendShapeCount; shape++)
            for (int frame = 0; frame < original.GetBlendShapeFrameCount(shape); frame++)
            {
                var dv = new Vector3[original.vertexCount]; var dn = new Vector3[original.vertexCount]; var dt = new Vector3[original.vertexCount];
                var bv = new Vector3[offset]; var bn = new Vector3[offset]; var bt = new Vector3[offset];
                original.GetBlendShapeFrameVertices(shape, frame, dv, dn, dt); reduced.GetBlendShapeFrameVertices(shape, frame, bv, bn, bt);
                mesh.AddBlendShapeFrame(original.GetBlendShapeName(shape), original.GetBlendShapeFrameWeight(shape, frame),
                    Join(bv, dv), Join(bn, dn), Join(bt, dt));
            }
        UnityEngine.Object.DestroyImmediate(body); UnityEngine.Object.DestroyImmediate(reduced); return mesh;
    }
    private static void Validate(Mesh original, Mesh derived, bool[]? locked)
    {
        if (derived.subMeshCount != original.subMeshCount || derived.bindposes.Length != original.bindposes.Length
            || derived.blendShapeCount != original.blendShapeCount) throw new InvalidDataException("Native material/rig/expression shape changed");
        for (int i = 0; i < original.bindposes.Length; i++)
            if (derived.bindposes[i] != original.bindposes[i]) throw new InvalidDataException("Native bind pose changed");
        for (int i = 0; i < original.blendShapeCount; i++)
            if (derived.GetBlendShapeName(i) != original.GetBlendShapeName(i)) throw new InvalidDataException("Native expression binding changed");
        if (locked == null) return;
        var retained = new HashSet<(Vector3 Position, Vector3 Normal, Vector2 Uv, BoneWeight Weight)>();
        Vector3[] positions = derived.vertices, normals = derived.normals; Vector2[] uv = derived.uv; BoneWeight[] weights = derived.boneWeights;
        for (int i = 0; i < positions.Length; i++) retained.Add((positions[i], normals[i], uv[i], weights[i]));
        positions = original.vertices; normals = original.normals; uv = original.uv; weights = original.boneWeights;
        var used = new bool[original.vertexCount];
        for (int sub = 0; sub < original.subMeshCount; sub++) foreach (int index in original.GetTriangles(sub)) used[index] = true;
        for (int i = 0; i < locked.Length; i++)
            if (locked[i] && used[i] && !retained.Contains((positions[i], normals[i], uv[i], weights[i])))
            {
                int samePosition = 0, sameNormal = 0, sameUv = 0, sameWeight = 0;
                foreach (var row in retained) if (row.Position == positions[i])
                { samePosition++; if (row.Normal == normals[i]) sameNormal++; if (row.Uv == uv[i]) sameUv++; if (row.Weight.Equals(weights[i])) sameWeight++; }
                throw new InvalidDataException("Protected native facial attributes changed: " + i
                    + " matchesPosition/normal/uv/weights=" + samePosition + "/" + sameNormal + "/" + sameUv + "/" + sameWeight);
            }
    }
    private static void Flush()
    {
        if (Part.Count == 0) return;
        string name = "ghvr-figure-meshes-distance-" + _number++.ToString("00") + ".bundle";
        Banks.Add(new AssetBundleBuild { assetBundleName = name, assetNames = Part.ToArray() });
        foreach (string path in Part) Index.Add(JsonUtility.ToJson(new Entry { mesh = Path.GetFileNameWithoutExtension(path), bank = name }));
        Part.Clear(); _bytes = 0;
    }
    private static int Triangles(Mesh mesh)
    { int count = 0; for (int i = 0; i < mesh.subMeshCount; i++) count += (int)mesh.GetIndexCount(i) / 3; return count; }
    [Serializable] private sealed class Sources { public Source[] meshes = Array.Empty<Source>(); }
    [Serializable] private sealed class Source { public string file = ""; public bool readable = false, npc = false; public int[] protectedBones = Array.Empty<int>(); }
    [Serializable] private sealed class Entry { public string mesh = "", bank = ""; }
    [Serializable] private sealed class Result
    { public string source = "", key = ""; public int tier, sourceVertices, vertices, sourceTriangles, triangles, blendShapes, protectedBones; }
}
