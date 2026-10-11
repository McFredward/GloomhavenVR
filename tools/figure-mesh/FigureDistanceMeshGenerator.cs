using System;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Core;
using UnityEditor;
using UnityEngine;

/// <summary>Append-only scenario far bank generation. Existing figure banks remain byte-for-byte
/// unchanged. Far surfaces allow a larger geometric error and interpolate body attributes, with
/// fixed open boundaries and native material slots. Immersive town NPCs are explicitly excluded.</summary>
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
            // Old reused extraction directories may still contain town rows. Never generate
            // their retired surfaces again, even with -skip-extract or stale exports.
            if (source.npc) continue;
            Mesh original = NativeFigureMeshStream.Read(Path.Combine(input, source.file));
            string key = ScenarioFigureMeshBank.Key(original, source.readable);
            Add(original, key, 5);
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
    private static void Add(Mesh original, string key, int tier)
    {
        var options = UnityMeshSimplifier.SimplificationOptions.Default;
        options.EnableSmartLink = true;
        options.PreserveBorderEdges = true;
        options.PreserveUVSeamEdges = false;
        options.PreserveUVFoldoverEdges = false;
        var simplifier = new UnityMeshSimplifier.MeshSimplifier(original);
        simplifier.SimplificationOptions = options;
        simplifier.SimplifyMesh(tier / 100f);
        Mesh derived = simplifier.ToMesh();
        derived.bounds = original.bounds;
        Validate(original, derived);
        int originalTriangles = Triangles(original), triangles = Triangles(derived);
        if (triangles >= originalTriangles * .97f || derived.vertexCount >= original.vertexCount)
        { UnityEngine.Object.DestroyImmediate(derived); return; }
        derived.name = key + "-" + tier;
        string path = "Assets/DistanceGenerated/" + derived.name + ".asset";
        AssetDatabase.CreateAsset(derived, path);
        long bytes = (long)derived.vertexCount * (128 + original.blendShapeCount * 36) + (long)triangles * 12;
        if (Part.Count >= 24 || _bytes + bytes > 16 * 1024 * 1024) Flush();
        Part.Add(path); _bytes += bytes;
        Report.Add(JsonUtility.ToJson(new Result { source = original.name, key = key, tier = tier,
            sourceVertices = original.vertexCount, vertices = derived.vertexCount, sourceTriangles = originalTriangles,
            triangles = triangles, blendShapes = original.blendShapeCount }));
        Debug.Log("Distance mesh " + key + " tier=" + tier + " triangles=" + originalTriangles + "->" + triangles);
    }
    private static void Validate(Mesh original, Mesh derived)
    {
        if (derived.subMeshCount != original.subMeshCount || derived.bindposes.Length != original.bindposes.Length
            || derived.blendShapeCount != original.blendShapeCount) throw new InvalidDataException("Native material/rig/expression shape changed");
        for (int i = 0; i < original.bindposes.Length; i++)
            if (derived.bindposes[i] != original.bindposes[i]) throw new InvalidDataException("Native bind pose changed");
        for (int i = 0; i < original.blendShapeCount; i++)
            if (derived.GetBlendShapeName(i) != original.GetBlendShapeName(i)) throw new InvalidDataException("Native expression binding changed");
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
    [Serializable] private sealed class Source { public string file = ""; public bool readable = false, npc = false; }
    [Serializable] private sealed class Entry { public string mesh = "", bank = ""; }
    [Serializable] private sealed class Result
    { public string source = "", key = ""; public int tier, sourceVertices, vertices, sourceTriangles, triangles, blendShapes; }
}
