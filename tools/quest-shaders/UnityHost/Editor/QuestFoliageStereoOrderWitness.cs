#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using GloomhavenVR.Quest.Editor;

public static class QuestFoliageStereoOrderWitness
{
    [Serializable] public sealed class Case { public string id, passType; public int subshader, pass, hardwareTier; public string[] keywords; }
    [Serializable] public sealed class Rewrite { public string assetPath, beforeSha256, afterSha256, candidatePath, metaSha256; }
    [Serializable] public sealed class Input { public string shaderPath, shaderGuid, shaderSha256, shaderMetaSha256; public Case[] failures, nativeControls; public Rewrite[] rewrites; }
    [Serializable] public sealed class Bank { public Case variant; public string bankSha256, vertexSha256, fragmentSha256, vertexFile, fragmentFile; public QuestVulkanShaderValidation.Interface[] inputs, outputs; public bool vertexEyeRoutingObserved, fragmentEyeRoutingObserved; }
    [Serializable] public sealed class Proof
    {
        public int schema = 1, originalInputRewriteCount, baselineRejectedCount, positiveCompilerCount, monoExactStageCount;
        public string unityVersion, shaderGuid, shaderSha256, shaderMetaSha256, inputSha256, actualBundleSha256;
        public bool originalDeclarationsAndMathPreserved, originalShaderAndMetaPreserved, actualNativeBundleBuilt, headsetPictureVerified, originalPixelParityVerified;
        public Bank[] banks;
    }
    static string Hash(string path) { using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
    static QuestVulkanShaderValidation.Result Compile(Shader shader, Case variant) { return QuestVulkanShaderValidation.Compile(shader, variant.subshader, variant.pass, variant.keywords, variant.hardwareTier); }
    public static void Run()
    {
        const string inputPath = "WitnessInput.json", output = "FoliageWitness";
        var input = JsonUtility.FromJson<Input>(File.ReadAllText(inputPath));
        var proof = new Proof { unityVersion = Application.unityVersion, shaderGuid = input.shaderGuid, shaderSha256 = input.shaderSha256,
            shaderMetaSha256 = input.shaderMetaSha256, inputSha256 = Hash(inputPath) };
        if (Application.unityVersion != "2021.3.5f1") throw new InvalidOperationException("Exact original Editor required.");
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
        if (AssetDatabase.AssetPathToGUID(input.shaderPath) != input.shaderGuid || Hash(input.shaderPath) != input.shaderSha256 || Hash(input.shaderPath + ".meta") != input.shaderMetaSha256)
            throw new InvalidOperationException("Exact original Shader identity changed.");
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(input.shaderPath);
        Directory.CreateDirectory(output);
        foreach (var variant in input.failures)
        {
            bool rejected = false;
            try { Compile(shader, variant); }
            catch (InvalidOperationException error) { rejected = error.Message.Contains("Non system-generated input signature parameter"); }
            if (!rejected) throw new InvalidOperationException("Original failing native permutation did not reproduce: " + variant.id);
            ++proof.baselineRejectedCount;
        }
        var original = input.nativeControls.ToDictionary(row => row.id, row => Compile(shader, row));
        foreach (var rewrite in input.rewrites)
        {
            if (Hash(rewrite.assetPath) != rewrite.beforeSha256 || Hash(rewrite.candidatePath) != rewrite.afterSha256 || Hash(rewrite.assetPath + ".meta") != rewrite.metaSha256)
                throw new InvalidOperationException("Witnessed include changed before exact declaration repair.");
            const string macro = "    UNITY_VERTEX_OUTPUT_STEREO\n";
            if (File.ReadAllText(rewrite.assetPath).Replace(macro, "") != File.ReadAllText(rewrite.candidatePath).Replace(macro, ""))
                throw new InvalidOperationException("Original declarations or math changed.");
            File.Copy(rewrite.candidatePath, rewrite.assetPath, true);
            ++proof.originalInputRewriteCount;
        }
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.ImportAsset(input.shaderPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        shader = AssetDatabase.LoadAssetAtPath<Shader>(input.shaderPath);
        var banks = new List<Bank>();
        foreach (var variant in input.failures.Concat(input.nativeControls))
        {
            var actual = Compile(shader, variant);
            string vertex = actual.vertexSha256 + ".vertex.spv", fragment = actual.fragmentSha256 + ".fragment.spv";
            File.WriteAllBytes(Path.Combine(output, vertex), actual.vertex); File.WriteAllBytes(Path.Combine(output, fragment), actual.fragment);
            foreach (var consumed in actual.inputs.Where(row => row.stage == "fragment" && row.location >= 0))
                if (!actual.outputs.Any(row => row.stage == "vertex" && row.location == consumed.location && row.componentType == consumed.componentType && row.components == consumed.components && row.flat == consumed.flat))
                    throw new InvalidOperationException("Actual recovered Vulkan stage link lost a native input: " + variant.id + " / " + consumed.location);
            QuestVulkanShaderValidation.Result before;
            if (original.TryGetValue(variant.id, out before))
            {
                if (before.vertexSha256 != actual.vertexSha256 || before.fragmentSha256 != actual.fragmentSha256)
                    throw new InvalidOperationException("Original mono/native stage instructions changed: " + variant.id);
                ++proof.monoExactStageCount;
            }
            banks.Add(new Bank { variant = variant, bankSha256 = actual.bankSha256, vertexSha256 = actual.vertexSha256, fragmentSha256 = actual.fragmentSha256,
                vertexFile = vertex, fragmentFile = fragment, inputs = actual.inputs, outputs = actual.outputs,
                vertexEyeRoutingObserved = actual.vertexEyeRoutingObserved, fragmentEyeRoutingObserved = actual.fragmentEyeRoutingObserved });
            ++proof.positiveCompilerCount;
        }
        var variants = new ShaderVariantCollection();
        foreach (var variant in input.failures.Concat(input.nativeControls))
            variants.Add(new ShaderVariantCollection.ShaderVariant(shader, (PassType)Enum.Parse(typeof(PassType), variant.passType), variant.keywords));
        AssetDatabase.CreateAsset(variants, "Assets/FoliageWitness.shadervariants");
        AssetDatabase.SaveAssets();
        string bundles = Path.Combine(output, "bundles"); Directory.CreateDirectory(bundles);
        var built = BuildPipeline.BuildAssetBundles(bundles, new[] { new AssetBundleBuild {
            assetBundleName = "native-foliage-declaration-order", assetNames = new[] { input.shaderPath, "Assets/FoliageWitness.shadervariants" }
        } }, BuildAssetBundleOptions.StrictMode | BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.Android);
        if (built == null || ShaderUtil.GetShaderMessages(shader).Any(row => row.severity == ShaderCompilerMessageSeverity.Error))
            throw new InvalidOperationException("Actual native Foliage bundle did not compile completely.");
        proof.actualNativeBundleBuilt = true; proof.actualBundleSha256 = Hash(Path.Combine(bundles, "native-foliage-declaration-order"));
        foreach (var rewrite in input.rewrites)
            if (Hash(rewrite.assetPath) != rewrite.afterSha256 || Hash(rewrite.assetPath + ".meta") != rewrite.metaSha256) throw new InvalidOperationException("Native include/meta changed after actual build.");
        if (Hash(input.shaderPath) != input.shaderSha256 || Hash(input.shaderPath + ".meta") != input.shaderMetaSha256) throw new InvalidOperationException("Original Shader/meta changed after actual build.");
        proof.originalDeclarationsAndMathPreserved = proof.originalShaderAndMetaPreserved = true;
        proof.banks = banks.ToArray();
        File.WriteAllText(Path.Combine(output, "results.json"), JsonUtility.ToJson(proof, true) + "\n");
        Debug.Log("PASS actual native Foliage declaration-order witness: original rejected=" + proof.baselineRejectedCount + ", repaired includes=" + proof.originalInputRewriteCount + ", positive banks=" + proof.positiveCompilerCount + ", byte-identical original mono/native stages=" + proof.monoExactStageCount + ", bundle=true; headset and pixels remain separate.");
    }
}
#endif
