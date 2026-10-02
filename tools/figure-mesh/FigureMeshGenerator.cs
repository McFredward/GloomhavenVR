using System;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Core;
using UnityEditor;
using UnityEngine;

public static class FigureMeshGenerator
{
    public static void Build()
    {
        var args = Environment.GetCommandLineArgs();
        string input = args[Array.IndexOf(args, "-nativeMeshInput") + 1];
        string output = args[Array.IndexOf(args, "-nativeMeshOutput") + 1];
        AssetDatabase.DeleteAsset("Assets/Generated");
        Directory.CreateDirectory("Assets/Generated"); Directory.CreateDirectory(output);
        var assets = new Dictionary<int, List<(string Path, long Bytes)>>(); var report = new List<string>(); var keys = new HashSet<string>();
        Sources sources = JsonUtility.FromJson<Sources>(File.ReadAllText(Path.Combine(input, "sources.json")));
        var readability = new Dictionary<string, bool>();
        foreach (Source source in sources.meshes) readability.Add(source.file, source.readable);
        foreach (int tier in new[] { 75, 45, 20 }) assets.Add(tier, new());
        string[] files = Directory.GetFiles(input, "*.mesh"); Array.Sort(files, StringComparer.Ordinal);
        foreach (string file in files)
        {
            Mesh original = NativeFigureMeshStream.Read(file);
            string key = ScenarioFigureMeshBank.Key(original, readability[Path.GetFileName(file)]);
            if (!keys.Add(key)) throw new InvalidDataException("Ambiguous native skeleton/atlas identity: " + original.name);
            int originalTriangles = Triangles(original);
            foreach (int tier in new[] { 75, 45, 20 })
            {
                try
                {
                    Mesh derived = ScenarioFigureMeshTopology.Simplify(original, tier / 100f, out int[] survivors);
                    // Preserve a meaningful real saving, never advertise a copy as coarse.
                    if (Triangles(derived) >= originalTriangles * .97f || derived.vertexCount >= original.vertexCount)
                    { UnityEngine.Object.DestroyImmediate(derived); continue; }
                    derived.name = key + "-" + tier;
                    string path = "Assets/Generated/" + derived.name + ".asset";
                    AssetDatabase.CreateAsset(derived, path);
                    assets[tier].Add((path, (long)derived.vertexCount * 128 + (long)Triangles(derived) * 12 + 8192));
                    File.WriteAllText(Path.Combine(output, derived.name + ".map"), string.Join(",", survivors));
                    report.Add(JsonUtility.ToJson(new Result { source = original.name, sourceFile = Path.GetFileName(file), key = key, tier = tier,
                        sourceVertices = original.vertexCount, vertices = derived.vertexCount, sourceTriangles = originalTriangles, triangles = Triangles(derived) }));
                }
                catch (ArgumentException error)
                { Debug.LogWarning("Retained original " + original.name + ": " + error.Message); break; }
            }
            UnityEngine.Object.DestroyImmediate(original);
        }
        AssetDatabase.SaveAssets();
        File.WriteAllText(Path.Combine(output, "results.json"), "[" + string.Join(",\n", report) + "]\n");
        BuildBanks(output, assets);
        EditorApplication.Exit(0);
    }
    /// <summary>Verify every serialized derivative, not just three representative runtime
    /// bodies. Survivor maps are regeneration evidence, never needed by the game.</summary>
    public static void Validate()
    {
        var args = Environment.GetCommandLineArgs();
        string input = args[Array.IndexOf(args, "-nativeMeshInput") + 1];
        string output = args[Array.IndexOf(args, "-nativeMeshOutput") + 1];
        Results results = JsonUtility.FromJson<Results>("{\"meshes\":" + File.ReadAllText(Path.Combine(output, "results.json")) + "}");
        int count = 0, checks = 0;
        foreach (Result result in results.meshes)
        {
            Mesh source = NativeFigureMeshStream.Read(Path.Combine(input, result.sourceFile));
            string asset = "Assets/Generated/" + result.key + "-" + result.tier + ".asset";
            Mesh derived = AssetDatabase.LoadAssetAtPath<Mesh>(asset);
            string[] indices = File.ReadAllText(Path.Combine(output, derived.name + ".map")).Split(',');
            var map = new int[indices.Length]; for (int i = 0; i < map.Length; i++) map[i] = int.Parse(indices[i]);
            void Require(bool value, string why)
            { checks++; if (!value) throw new InvalidDataException(derived.name + ": " + why); }
            void Channel<T>(T[] original, T[] values) where T : IEquatable<T>
            {
                Require(original.Length == 0 ? values.Length == 0 : values.Length == map.Length, "channel dimensions");
                if (original.Length == 0) return;
                for (int i = 0; i < map.Length; i++) Require(map[i] >= 0 && map[i] < original.Length && values[i].Equals(original[map[i]]), "exact survivor attribute");
            }
            Require(derived.vertexCount == map.Length && derived.vertexCount == result.vertices && Triangles(derived) == result.triangles, "recorded geometry counts");
            Require(derived.bounds == source.bounds && derived.subMeshCount == source.subMeshCount && derived.blendShapeCount == source.blendShapeCount, "bounds/material/shape identity");
            Channel(source.vertices, derived.vertices); Channel(source.normals, derived.normals);
            Channel(source.tangents, derived.tangents); Channel(source.colors, derived.colors); Channel(source.boneWeights, derived.boneWeights);
            for (int uv = 0; uv < 8; uv++)
            {
                var originalUV = new List<Vector4>(); var derivedUV = new List<Vector4>();
                source.GetUVs(uv, originalUV); derived.GetUVs(uv, derivedUV); Channel(originalUV.ToArray(), derivedUV.ToArray());
            }
            Matrix4x4[] originalPoses = source.bindposes, derivedPoses = derived.bindposes;
            Require(originalPoses.Length == derivedPoses.Length, "bone dimensions");
            for (int bone = 0; bone < originalPoses.Length; bone++)
                for (int row = 0; row < 4; row++) for (int column = 0; column < 4; column++)
                    Require(originalPoses[bone][row, column].Equals(derivedPoses[bone][row, column]), "exact bindpose scalar");
            Dictionary<ulong, int> Edges(Mesh mesh, int[]? originalIndices)
            {
                var edges = new Dictionary<ulong, int>();
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    int[] triangles = mesh.GetTriangles(sub);
                    foreach (int index in triangles) Require(index >= 0 && index < mesh.vertexCount, "valid submesh vertex");
                    for (int i = 0; i < triangles.Length; i += 3) for (int side = 0; side < 3; side++)
                    {
                        int a = triangles[i + side], b = triangles[i + (side + 1) % 3];
                        if (originalIndices != null) { a = originalIndices[a]; b = originalIndices[b]; }
                        ulong key = ((ulong)(uint)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);
                        edges.TryGetValue(key, out int edgesCount); edges[key] = edgesCount + 1;
                    }
                }
                return edges;
            }
            var before = Edges(source, null); var after = Edges(derived, map);
            foreach (var edge in before) if (edge.Value == 1)
                Require(after.TryGetValue(edge.Key, out int value) && value == 1, "original boundary retained");
            foreach (var edge in after) if (edge.Value == 1)
                Require(before.TryGetValue(edge.Key, out int value) && value == 1, "no new hole boundary");
            UnityEngine.Object.DestroyImmediate(source); count++;
        }
        File.WriteAllText(Path.Combine(output, "validation.json"), "{\"derivatives\":" + count + ",\"assertions\":" + checks + "}\n");
        Debug.Log("Verified " + count + " serialized native derivatives with " + checks + " attribute/boundary assertions");
        EditorApplication.Exit(0);
    }
    public static void Repack()
    {
        var args = Environment.GetCommandLineArgs();
        string output = args[Array.IndexOf(args, "-nativeMeshOutput") + 1];
        var assets = new Dictionary<int, List<(string Path, long Bytes)>>();
        foreach (int tier in new[] { 75, 45, 20 }) assets.Add(tier, new());
        string[] files = Directory.GetFiles("Assets/Generated", "*.asset"); Array.Sort(files, StringComparer.Ordinal);
        foreach (string path in files)
        {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            int tier = int.Parse(mesh.name.Substring(mesh.name.LastIndexOf('-') + 1));
            assets[tier].Add((path, (long)mesh.vertexCount * 128 + (long)Triangles(mesh) * 12 + 8192));
        }
        BuildBanks(output, assets);
        EditorApplication.Exit(0);
    }
    private static void BuildBanks(string output, Dictionary<int, List<(string Path, long Bytes)>> assets)
    {
        foreach (string old in Directory.GetFiles(output, "ghvr-figure-meshes-*.bundle*")) File.Delete(old);
        var banks = new List<AssetBundleBuild>(); int count = 0; var index = new List<string>();
        foreach (int tier in new[] { 75, 45, 20 })
        {
            var part = new List<string>(); long bytes = 0; int number = 0;
            void AddPart()
            {
                if (part.Count == 0) return;
                string filename = "ghvr-figure-meshes-" + tier + "-" + number++.ToString("00") + ".bundle";
                banks.Add(new AssetBundleBuild { assetBundleName = filename, assetNames = part.ToArray() });
                foreach (string path in part) index.Add(JsonUtility.ToJson(new IndexEntry { mesh = Path.GetFileNameWithoutExtension(path), bank = filename }));
                part.Clear(); bytes = 0;
            }
            foreach (var entry in assets[tier])
            {
                if (part.Count >= 32 || bytes + entry.Bytes > 16 * 1024 * 1024) AddPart();
                part.Add(entry.Path); bytes += entry.Bytes; count++;
            }
            AddPart();
        }
        BuildPipeline.BuildAssetBundles(output, banks.ToArray(),
            BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.DeterministicAssetBundle, BuildTarget.StandaloneWindows64);
        File.WriteAllText(Path.Combine(output, "ghvr-figure-meshes-index.json"), "{\"entries\":[" + string.Join(",\n", index) + "]}\n");
        Debug.Log("Generated " + count + " original actor mesh derivatives in " + banks.Count + " banks");
    }
    private static int Triangles(Mesh mesh)
    { int result = 0; for (int i = 0; i < mesh.subMeshCount; i++) result += (int)mesh.GetIndexCount(i) / 3; return result; }
    [Serializable] private sealed class Result
    { public string source = "", sourceFile = "", key = ""; public int tier, sourceVertices, vertices, sourceTriangles, triangles; }
    [Serializable] private sealed class IndexEntry { public string mesh = "", bank = ""; }
    [Serializable] private sealed class Results { public Result[] meshes = Array.Empty<Result>(); }
    [Serializable] private sealed class Sources { public Source[] meshes = Array.Empty<Source>(); }
    [Serializable] private sealed class Source { public string file = ""; public bool readable = false; }
}
