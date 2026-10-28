using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using GloomhavenVR.Core;
using UnityEditor;
using UnityEngine;

/// <summary>Owned Android figure derivatives. The existing Frame mesh/index contract is
/// retained; completed source meshes survive an interrupted Unity build independently.</summary>
public static class QuestFigureMeshGenerator
{
    [Serializable] private sealed class Sources { public Source[] meshes = Array.Empty<Source>(); }
    [Serializable] private sealed class Source { public string file = "", geometry_sha256 = ""; public bool readable = false, npc = false; }
    [Serializable] private sealed class Completion { public string producerKey = "", source = ""; public string[] assets = Array.Empty<string>(), hashes = Array.Empty<string>(); }
    [Serializable] private sealed class Entry { public string mesh = "", bank = ""; }
    [Serializable] private sealed class Index { public Entry[] entries = Array.Empty<Entry>(); }
    private static readonly int[] Tiers = { 75, 45, 20, 5 };

    public static void Build()
    {
        string[] args = Environment.GetCommandLineArgs();
        string Arg(string key) => args[Array.IndexOf(args, key) + 1];
        Build(Arg("-nativeMeshInput"), Arg("-nativeMeshOutput"), Arg("-figureProducerKey"));
        EditorApplication.Exit(0);
    }

    public static void Build(string input, string output, string producerKey)
    {
        if (producerKey.Length != 64) throw new InvalidDataException("Invalid figure producer identity");
        string cache = Path.Combine(Path.GetDirectoryName(output)!, "derivatives");
        Directory.CreateDirectory(cache); Directory.CreateDirectory(output);
        Directory.CreateDirectory("Assets/QuestFigures");
        Sources sources = JsonUtility.FromJson<Sources>(File.ReadAllText(Path.Combine(input, "sources.json")));
        var assets = new Dictionary<int, SortedDictionary<string, long>>();
        foreach (int tier in Tiers) assets.Add(tier, new SortedDictionary<string, long>(StringComparer.Ordinal));
        var identities = new HashSet<string>();
        for (int i = 0; i < sources.meshes.Length; i++)
        {
            Source source = sources.meshes[i];
            string completionFile = Path.Combine(cache, source.geometry_sha256 + ".json");
            Completion? completed = File.Exists(completionFile)
                ? JsonUtility.FromJson<Completion>(File.ReadAllText(completionFile)) : null;
            bool reuse = completed != null && completed.producerKey == producerKey && completed.source == source.geometry_sha256
                && completed.assets.Length == completed.hashes.Length;
            if (reuse) for (int asset = 0; asset < completed!.assets.Length; asset++)
            {
                string name = completed.assets[asset], path = Path.Combine(cache, name);
                reuse &= name == Path.GetFileName(name) && File.Exists(path) && Hash(path) == completed.hashes[asset];
            }
            if (!reuse)
            {
                Mesh original = NativeFigureMeshStream.Read(Path.Combine(input, source.file));
                string key = ScenarioFigureMeshBank.Key(original, source.readable);
                var created = new List<string>();
                try
                {
                    foreach (int tier in Tiers)
                    {
                        if (tier == 5 && source.npc) continue;
                        Mesh? derived = null;
                        try
                        {
                            if (tier == 5)
                            {
                                var options = UnityMeshSimplifier.SimplificationOptions.Default;
                                options.EnableSmartLink = true; options.PreserveBorderEdges = true;
                                options.PreserveUVSeamEdges = false; options.PreserveUVFoldoverEdges = false;
                                var simplifier = new UnityMeshSimplifier.MeshSimplifier(original) { SimplificationOptions = options };
                                simplifier.SimplifyMesh(.05f); derived = simplifier.ToMesh(); derived.bounds = original.bounds;
                            }
                            else derived = ScenarioFigureMeshTopology.Simplify(original, tier / 100f, out _);
                            Validate(original, derived);
                            if (Triangles(derived) >= Triangles(original) * .97f || derived.vertexCount >= original.vertexCount)
                            { UnityEngine.Object.DestroyImmediate(derived); derived = null; continue; }
                            derived.name = key + "-" + tier;
                            string name = derived.name + ".asset", path = "Assets/QuestFigures/" + name;
                            AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(derived, path);
                            AssetDatabase.SaveAssets();
                            // Atomic source publication, not a receipt for unfinished work.
                            string temporary = Path.Combine(cache, name + ".tmp");
                            File.Copy(path, temporary, true); Replace(temporary, Path.Combine(cache, name));
                            created.Add(name); derived = null; // AssetDatabase owns the persistent mesh.
                        }
                        catch (ArgumentException error)
                        { Debug.LogWarning("[Quest figures] Retained original " + original.name + " tier=" + tier + ": " + error.Message); }
                        finally { if (derived != null) UnityEngine.Object.DestroyImmediate(derived); }
                    }
                    var hashes = new List<string>(); foreach (string name in created) hashes.Add(Hash(Path.Combine(cache, name)));
                    completed = new Completion { producerKey = producerKey, source = source.geometry_sha256, assets = created.ToArray(), hashes = hashes.ToArray() };
                    File.WriteAllText(completionFile + ".tmp", JsonUtility.ToJson(completed));
                    Replace(completionFile + ".tmp", completionFile);
                }
                finally { UnityEngine.Object.DestroyImmediate(original); }
            }
            foreach (string name in completed!.assets)
            {
                if (!identities.Add(name)) throw new InvalidDataException("Ambiguous original figure identity: " + name);
                string path = "Assets/QuestFigures/" + name;
                string expectedHash = completed.hashes[Array.IndexOf(completed.assets, name)];
                if (!File.Exists(path) || Hash(path) != expectedHash) File.Copy(Path.Combine(cache, name), path, true);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (mesh == null) throw new InvalidDataException("Cached figure mesh is unreadable: " + name);
                int tier = int.Parse(mesh.name.Substring(mesh.name.LastIndexOf('-') + 1));
                assets[tier].Add(path, (long)mesh.vertexCount * 128 + (long)Triangles(mesh) * 12 + 8192);
            }
            Debug.Log("GHVRQ_FIGURES source " + (i + 1) + "/" + sources.meshes.Length + " " + source.file + " reused=" + reuse);
        }
        var entries = new List<Entry>();
        var banks = new List<AssetBundleBuild>();
        foreach (int tier in Tiers)
        {
            var part = new List<string>(); long bytes = 0; int number = 0;
            void Flush()
            {
                if (part.Count == 0) return;
                string name = "ghvr-figure-meshes-quest-" + tier + "-" + number++.ToString("00") + ".bundle";
                banks.Add(new AssetBundleBuild { assetBundleName = name, assetNames = part.ToArray() });
                foreach (string path in part) entries.Add(new Entry { mesh = Path.GetFileNameWithoutExtension(path), bank = name });
                part.Clear(); bytes = 0;
            }
            foreach (var asset in assets[tier])
            {
                if (part.Count >= 24 || bytes + asset.Value > 16 * 1024 * 1024) Flush();
                part.Add(asset.Key); bytes += asset.Value;
            }
            Flush();
        }
        if (banks.Count == 0) throw new InvalidDataException("Owned figure extraction produced no useful Android derivatives");
        // One part per call is a durable unit and exposes a real denominator.
        for (int i = 0; i < banks.Count; i++)
        {
            string published = Path.Combine(output, banks[i].assetBundleName);
            string proof = producerKey + ":" + string.Join(",", banks[i].assetNames);
            string receipt = published + ".receipt";
            if (File.Exists(published) && File.Exists(receipt) && File.ReadAllText(receipt) == proof + ":" + Hash(published))
            {
                Debug.Log("GHVRQ_FIGURES bank " + (i + 1) + "/" + banks.Count + " " + banks[i].assetBundleName + " reused=True");
                continue;
            }
            // Unity names its manifest bundle after the output directory. It
            // must not collide with the user bank's filename.
            string partOutput = Path.Combine(output, "parts", banks[i].assetBundleName, "build");
            Directory.CreateDirectory(partOutput);
            var result = BuildPipeline.BuildAssetBundles(partOutput, new[] { banks[i] },
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.DeterministicAssetBundle, BuildTarget.Android);
            if (result == null) throw new InvalidDataException("Android figure part build failed: " + banks[i].assetBundleName);
            string built = Path.Combine(partOutput, banks[i].assetBundleName);
            File.Copy(built, Path.Combine(output, banks[i].assetBundleName + ".tmp"), true);
            Replace(Path.Combine(output, banks[i].assetBundleName + ".tmp"), Path.Combine(output, banks[i].assetBundleName));
            File.WriteAllText(receipt + ".tmp", proof + ":" + Hash(published)); Replace(receipt + ".tmp", receipt);
            // The final immutable bank + receipt owns retry reuse. Unity's
            // temporary part directory need not keep a second bank copy.
            File.Delete(built);
            Debug.Log("GHVRQ_FIGURES bank " + (i + 1) + "/" + banks.Count + " " + banks[i].assetBundleName);
        }
        File.WriteAllText(Path.Combine(output, "ghvr-figure-meshes-index.json.tmp"), JsonUtility.ToJson(new Index { entries = entries.ToArray() }));
        Replace(Path.Combine(output, "ghvr-figure-meshes-index.json.tmp"), Path.Combine(output, "ghvr-figure-meshes-index.json"));
        Debug.Log("GHVRQ_FIGURES complete " + entries.Count + " derivatives " + banks.Count + " Android banks");
    }

    private static void Replace(string temporary, string target)
    { if (File.Exists(target)) File.Replace(temporary, target, null); else File.Move(temporary, target); }
    private static string Hash(string path)
    { using var hash = SHA256.Create(); using var input = File.OpenRead(path); return BitConverter.ToString(hash.ComputeHash(input)).Replace("-", "").ToLowerInvariant(); }
    private static int Triangles(Mesh mesh)
    { int count = 0; for (int i = 0; i < mesh.subMeshCount; i++) count += (int)mesh.GetIndexCount(i) / 3; return count; }
    private static void Validate(Mesh source, Mesh derived)
    {
        if (source.subMeshCount != derived.subMeshCount || source.blendShapeCount != derived.blendShapeCount
            || source.bounds != derived.bounds || source.bindposes.Length != derived.bindposes.Length)
            throw new InvalidDataException("Original figure slots/rig/bounds changed");
        for (int i = 0; i < source.bindposes.Length; i++)
            if (source.bindposes[i] != derived.bindposes[i]) throw new InvalidDataException("Original figure bind pose changed");
    }
}
