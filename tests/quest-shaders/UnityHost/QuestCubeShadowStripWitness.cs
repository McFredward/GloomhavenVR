#if UNITY_EDITOR && GHVR_QUEST_GAME
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

public static class QuestCubeShadowStripWitness
{
    [Serializable] public sealed class Variant { public int subshader, pass, hardwareTier; public string passType; public string[] keywords; }
    [Serializable] public sealed class ShaderRow { public string assetPath, guid; }
    [Serializable] public sealed class Input { public ShaderRow shader; public Variant[] controls; public string originalManifestSha256, shaderSha256; }
    [Serializable] public sealed class Bank { public string[] keywords; public string vertexSha256, fragmentSha256; }
    [Serializable] public sealed class Receipt
    {
        public int schema = 1, originalAliasCount, originalShaderCount, eligibleShaderCount, nativeControls, removedCompilerProducts, negativeControls, generatedLightingProducts, unsupportedLightingProducts;
        public string unityVersion, originalManifestSha256, shaderSha256, actualBundleSha256;
        public bool invalidBankRejected, actualNativeBundleBuilt, originalNativeProgramsUnchanged, headsetPictureVerified;
        public Bank[] banks;
    }
    static string Hash(string path) { using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
    static void Require(bool test, string message) { if (!test) throw new InvalidOperationException(message); }
    public static void Run()
    {
        Require(Application.unityVersion == "2021.3.5f1", "Original Editor version required.");
        var input = JsonUtility.FromJson<Input>(File.ReadAllText("WitnessInput.json"));
        var proof = new Receipt { unityVersion = Application.unityVersion, originalManifestSha256 = Hash("FullOriginalManifest.json"), shaderSha256 = Hash(input.shader.assetPath) };
        Require(proof.originalManifestSha256 == input.originalManifestSha256 && proof.shaderSha256 == input.shaderSha256, "Exact native source identity changed.");
        var full = JsonUtility.FromJson<QuestCampaignShaderStrip.Manifest>(File.ReadAllText("FullOriginalManifest.json"));
        proof.originalShaderCount = full.shaders.Length;
        proof.originalAliasCount = full.shaders.Sum(shader => shader.variants.Length);
        var eligible = QuestCampaignShaderStrip.Audit(full);
        proof.eligibleShaderCount = eligible.Count;
        foreach (var shader in full.shaders)
            if (eligible.ContainsKey(shader.guid))
                foreach (var bank in shader.variants)
                    Require(!QuestCampaignShaderStrip.RejectLightingProduct(shader, (PassType)Enum.Parse(typeof(PassType), bank.passType), bank.keywords), "Original bank was removed.");
        var audited = eligible[input.shader.guid];
        string[] invalid = { "DIRECTIONAL", "SHADOWS_CUBE" };
        foreach (PassType pass in new[] { PassType.ForwardBase, PassType.ShadowCaster, PassType.Normal, PassType.Deferred })
        { Require(!QuestCampaignShaderStrip.RejectLightingProduct(audited, pass, invalid), "Other pass was stripped."); ++proof.negativeControls; }
        foreach (var keys in new[] { new[] { "POINT", "SHADOWS_CUBE" }, new[] { "POINT_COOKIE", "SHADOWS_CUBE" }, new[] { "SPOT", "SHADOWS_DEPTH" }, new[] { "DIRECTIONAL", "SHADOWS_SCREEN" } })
        { Require(!QuestCampaignShaderStrip.RejectLightingProduct(audited, PassType.ForwardAdd, keys), "Valid or unknown keyword bank was stripped."); ++proof.negativeControls; }
        Require(QuestCampaignShaderStrip.RejectLightingProduct(audited, PassType.ForwardAdd, invalid), "Known invalid product was retained.");
        var contradicts = JsonUtility.FromJson<QuestCampaignShaderStrip.Manifest>(File.ReadAllText("Assets/QuestOriginalCampaign/campaign-shaders.json"));
        contradicts.shaders[0].variants[0] = new QuestCampaignShaderStrip.Bank { passType = "ForwardAdd", keywords = invalid };
        bool rejected = false; try { QuestCampaignShaderStrip.Audit(contradicts); } catch (InvalidDataException) { rejected = true; }
        Require(rejected, "Changed native graph was silently stripped."); ++proof.negativeControls;

        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
        var shaderObject = AssetDatabase.LoadAssetAtPath<Shader>(input.shader.assetPath);
        Require(shaderObject != null && AssetDatabase.AssetPathToGUID(input.shader.assetPath) == input.shader.guid, "Exact imported Shader GUID missing.");
        try { QuestVulkanShaderValidation.Compile(shaderObject, 0, input.controls[0].pass, invalid, 0); }
        catch (InvalidOperationException error) { proof.invalidBankRejected = error.Message.Contains("No exact original native keyword bank is available"); }
        Require(proof.invalidBankRejected, "Actual native unsupported-product negative did not reproduce.");
        var banks = new List<Bank>();
        foreach (var control in input.controls)
        {
            var actual = QuestVulkanShaderValidation.Compile(shaderObject, control.subshader, control.pass, control.keywords, control.hardwareTier);
            banks.Add(new Bank { keywords = control.keywords, vertexSha256 = actual.vertexSha256, fragmentSha256 = actual.fragmentSha256 });
        }
        AssetDatabase.ImportAsset(input.shader.assetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        shaderObject = AssetDatabase.LoadAssetAtPath<Shader>(input.shader.assetPath);
        var collection = new ShaderVariantCollection();
        foreach (var control in input.controls)
            collection.Add(new ShaderVariantCollection.ShaderVariant(shaderObject, PassType.ForwardAdd, control.keywords));
        var shadowKeys = new[] { "SHADOWS_CUBE", "SHADOWS_DEPTH", "SHADOWS_SCREEN", "SHADOWS_SOFT" };
        foreach (var light in new[] { "DIRECTIONAL", "DIRECTIONAL_COOKIE", "POINT", "POINT_COOKIE", "SPOT" })
            for (int mask = 0; mask < 16; ++mask)
            {
                var keys = new List<string> { light };
                for (int bit = 0; bit < shadowKeys.Length; ++bit) if ((mask & (1 << bit)) != 0) keys.Add(shadowKeys[bit]);
                if (QuestCampaignShaderStrip.RejectLightingProduct(audited, PassType.ForwardAdd, keys)) ++proof.unsupportedLightingProducts;
                ++proof.generatedLightingProducts;
                collection.Add(new ShaderVariantCollection.ShaderVariant(shaderObject, PassType.ForwardAdd, keys.ToArray()));
            }
        collection.Add(new ShaderVariantCollection.ShaderVariant(shaderObject, PassType.ForwardAdd, new[] { "DIRECTIONAL", "SHADOWS_CUBE", "STEREO_MULTIVIEW_ON" }));
        AssetDatabase.CreateAsset(collection, "Assets/CubeStripWitness.shadervariants");
        AssetDatabase.SaveAssets();
        const string output = "CubeStripWitness";
        Directory.CreateDirectory(output + "/bundles");
        var bundle = BuildPipeline.BuildAssetBundles(output + "/bundles", new[] { new AssetBundleBuild {
            assetBundleName = "native-cube-shadow-strip", assetNames = new[] { input.shader.assetPath, "Assets/CubeStripWitness.shadervariants" }
        } }, BuildAssetBundleOptions.StrictMode | BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.Android);
        Require(bundle != null, "Actual native bundle failed.");
        proof.removedCompilerProducts = QuestCampaignShaderStrip.RemovedCount;
        Require(proof.removedCompilerProducts > 0, "Native bundle did not exercise the actual preprocess callback.");
        for (int index = 0; index < input.controls.Length; ++index)
        {
            var control = input.controls[index];
            var actual = QuestVulkanShaderValidation.Compile(shaderObject, control.subshader, control.pass, control.keywords, control.hardwareTier);
            Require(actual.vertexSha256 == banks[index].vertexSha256 && actual.fragmentSha256 == banks[index].fragmentSha256, "Original native programs changed.");
            ++proof.nativeControls;
        }
        Require(Hash(input.shader.assetPath) == proof.shaderSha256, "Original ShaderLab source changed.");
        proof.actualNativeBundleBuilt = proof.originalNativeProgramsUnchanged = true;
        proof.actualBundleSha256 = Hash(output + "/bundles/native-cube-shadow-strip"); proof.banks = banks.ToArray();
        File.WriteAllText(output + "/result.json", JsonUtility.ToJson(proof, true) + "\n");
        Debug.Log("PASS exact native cube-shadow exclusion: " + proof.originalAliasCount + " original aliases retained; " + proof.removedCompilerProducts + " impossible products excluded.");
    }
}
#endif
