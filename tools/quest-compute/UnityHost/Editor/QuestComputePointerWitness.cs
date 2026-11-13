using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public static class QuestComputePointerWitness
{
    [Serializable] sealed class Compute { public string name, guid, assetPath; public long localFileId; }
    [Serializable] sealed class Computes { public Compute[] shaders; }
    [Serializable] sealed class Field { public string path, guid, computeName; public long localFileId; public bool resolved; public int instanceId; }
    [Serializable] sealed class Result
    {
        public int schema = 1, shaderCount, ownerReferenceCount, changedTypeTokens;
        public string unityVersion, ownerPath, beforeSha256, afterSha256;
        public bool baselineType2Rejected, correctedType3Resolved, unchangedOtherOwnerBytes,
            unchangedComputeSourcesAndMetas, playerBuilt = false, headsetVerified = false;
        public Field[] before, after;
    }
    const string Owner = "Assets/QuestRecoveredBundles/8fea891106506cb4ab1a25d210912af1/PostProcessResources.asset";
    const string Output = "PointerWitness";
    static string Hash(byte[] bytes) { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    public static void Run()
    {
        if (Application.unityVersion != "2021.3.5f1") throw new InvalidOperationException("Wrong Unity importer version.");
        Directory.CreateDirectory(Output);
        var manifest = JsonUtility.FromJson<Computes>(File.ReadAllText("FixtureComputes.json"));
        if (manifest.shaders.Length != 13) throw new InvalidOperationException("Incomplete native compute witness.");
        var sources = new Dictionary<string, string>();
        foreach (var row in manifest.shaders)
        {
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(row.assetPath);
            string guid; long localId;
            if (shader == null || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(shader, out guid, out localId)
                || guid != row.guid || localId != row.localFileId || shader.name != row.name)
                throw new InvalidOperationException("Actual imported ComputeShader identity changed: " + row.assetPath);
            foreach (string path in new[] {row.assetPath, row.assetPath + ".meta"}) sources[path] = Hash(File.ReadAllBytes(path));
        }
        byte[] beforeBytes = File.ReadAllBytes(Owner);
        string beforeText = System.Text.Encoding.UTF8.GetString(beforeBytes);
        var before = ReadOwner();
        var result = new Result { unityVersion = Application.unityVersion, ownerPath = Owner,
            shaderCount = manifest.shaders.Length, ownerReferenceCount = before.Length,
            beforeSha256 = Hash(beforeBytes), before = before };
        File.WriteAllText(Path.Combine(Output, "baseline.json"), JsonUtility.ToJson(result, true) + "\n");
        if (before.Length != 12 || before.Any(row => row.resolved))
            throw new InvalidOperationException("Expected original type2 importer-reference negative control did not fail.");
        result.baselineType2Rejected = true;
        var targets = new HashSet<string>(manifest.shaders.Select(row => row.guid));
        string afterText = Regex.Replace(beforeText, @"\{fileID: 7200000, guid: ([0-9a-f]{32}), type: (2)\}", match => {
            if (!targets.Contains(match.Groups[1].Value)) return match.Value;
            ++result.changedTypeTokens;
            return match.Value.Substring(0, match.Groups[2].Index - match.Index) + "3" + match.Value.Substring(match.Groups[2].Index - match.Index + 1);
        });
        if (result.changedTypeTokens != 12) throw new InvalidOperationException("Original owner compute pointer census changed.");
        File.WriteAllText(Owner, afterText, new System.Text.UTF8Encoding(false));
        AssetDatabase.ImportAsset(Owner, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        result.after = ReadOwner();
        foreach (var row in result.after)
        {
            var expected = manifest.shaders.Single(value => value.guid == row.guid);
            if (!row.resolved || row.localFileId != expected.localFileId || row.computeName != expected.name)
                throw new InvalidOperationException("Exact type3 imported owner reference is unresolved: " + row.path);
        }
        result.correctedType3Resolved = result.after.Length == 12;
        result.afterSha256 = Hash(File.ReadAllBytes(Owner));
        string reversed = Regex.Replace(afterText, @"\{fileID: 7200000, guid: ([0-9a-f]{32}), type: 3\}", match =>
            targets.Contains(match.Groups[1].Value) ? match.Value.Substring(0, match.Value.Length - 2) + "2}" : match.Value);
        result.unchangedOtherOwnerBytes = reversed == beforeText;
        result.unchangedComputeSourcesAndMetas = sources.All(row => Hash(File.ReadAllBytes(row.Key)) == row.Value);
        if (!result.unchangedOtherOwnerBytes || !result.unchangedComputeSourcesAndMetas)
            throw new InvalidOperationException("Pointer type repair changed unaudited asset bytes.");
        File.WriteAllText(Path.Combine(Output, "results.json"), JsonUtility.ToJson(result, true) + "\n");
        UnityEngine.Debug.Log("PASS exact compute importer references: native shaders=13, type2 rejected=12, type3 resolved=12, native source/meta bytes unchanged.");
    }
    static Field[] ReadOwner()
    {
        var owner = AssetDatabase.LoadMainAssetAtPath(Owner);
        if (owner == null || owner.GetType().FullName != "UnityEngine.Rendering.PostProcessing.PostProcessResources")
            throw new InvalidOperationException("Actual original PostProcessResources script identity was not imported.");
        var serialized = new SerializedObject(owner);
        var iterator = serialized.GetIterator();
        var fields = new List<Field>();
        while (iterator.Next(true))
        {
            if (!iterator.propertyPath.StartsWith("computeShaders.", StringComparison.Ordinal)
                || iterator.propertyType != SerializedPropertyType.ObjectReference) continue;
            var value = iterator.objectReferenceValue;
            var row = new Field { path = iterator.propertyPath, resolved = value != null,
                instanceId = iterator.objectReferenceInstanceIDValue };
            if (value != null)
            {
                if (!(value is ComputeShader) || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out row.guid, out row.localFileId))
                    throw new InvalidOperationException("Original compute field resolved to the wrong native type.");
                row.computeName = value.name;
            }
            fields.Add(row);
        }
        serialized.Dispose();
        return fields.ToArray();
    }
}
