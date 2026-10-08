#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Require actual Android Vulkan/GLES3 compilation of the complete native compute bank.</summary>
public static class QuestCampaignComputeValidation
{
    const string DefaultManifest = "Assets/QuestOriginalCampaign/campaign-computes.json";
    const string DefaultReceipt = "Temp/QuestCampaignComputeValidation/compiled.json";
    [Serializable] sealed class Recovery
    {
        public int schema, shaderCount, kernelCount;
        public string graphicsApi;
        public ShaderContract[] shaders;
    }
    [Serializable] sealed class ShaderContract
    {
        public string name, assetPath, guid, sourceSha256, metaSha256;
        public int classId, localFileId, kernelCount;
        public KernelContract[] kernels;
    }
    [Serializable] sealed class KernelContract
    {
        public string name;
        public int[] threadGroups;
    }
    [Serializable] public sealed class CompilationReceipt
    {
        public int schema = 1, shaderCount, kernelCount;
        public string unityVersion, buildTarget, graphicsApi, recoveryManifestSha256;
        public bool androidCompiled, allOriginalKernelIdentitiesRetained, hardwareVerified;
        public ShaderCompilation[] shaders;
    }
    [Serializable] public sealed class ShaderCompilation
    {
        public string name, assetPath, guid;
        public long localFileId;
        public int kernelCount;
        public string[] platforms, compiledKernels, compiledGlesKernels, compiledVulkanKernels, warnings;
    }

    static string Sha(string path)
    {
        using (var hash = SHA256.Create())
            return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
    }
    static MethodInfo Api(string name)
    {
        var result = typeof(ShaderUtil).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
        if (result == null) throw new InvalidOperationException("Required Unity compute compiler audit API missing: " + name);
        return result;
    }
    static T Call<T>(string name, params object[] args) { return (T)Api(name).Invoke(null, args); }
    static Recovery Read(string manifestPath)
    {
        var result = JsonUtility.FromJson<Recovery>(File.ReadAllText(manifestPath));
        if (result == null || result.schema != 1 || result.shaderCount != 13 || result.kernelCount != 36
            || result.shaders == null || result.shaders.Length != 13
            || result.shaders.Select(shader => shader.name).Distinct().Count() != 13
            || result.shaders.Any(shader => shader.kernels == null || shader.kernelCount != shader.kernels.Length)
            || result.shaders.Sum(shader => shader.kernelCount) != 36)
            throw new InvalidDataException("Unknown complete original compute recovery contract.");
        return result;
    }

    static GraphicsDeviceType GraphicsApi(Recovery recovery)
    {
        if (recovery.graphicsApi == "Vulkan") return GraphicsDeviceType.Vulkan;
        if (string.IsNullOrEmpty(recovery.graphicsApi) || recovery.graphicsApi == "OpenGLES3") return GraphicsDeviceType.OpenGLES3;
        throw new InvalidDataException("Unsupported original compute graphics API.");
    }

    /// <summary>Call after the normal Android bank/player build has compiled shaders.</summary>
    public static CompilationReceipt Validate()
    {
        return Validate(Environment.GetEnvironmentVariable("GHVR_QUEST_COMPUTE_MANIFEST") ?? DefaultManifest,
            Environment.GetEnvironmentVariable("GHVR_QUEST_COMPUTE_RECEIPT") ?? DefaultReceipt);
    }

    /// <summary>Run before Addressables; source/identity checks alone are not a compiled-bank receipt.</summary>
    public static void ValidateSources()
    {
        Inspect(Environment.GetEnvironmentVariable("GHVR_QUEST_COMPUTE_MANIFEST") ?? DefaultManifest, false);
    }

    public static CompilationReceipt Validate(string manifestPath, string receiptPath)
    {
        var receipt = Inspect(manifestPath, true);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(receiptPath)));
        File.WriteAllText(receiptPath, JsonUtility.ToJson(receipt, true) + "\n");
        return receipt;
    }

    static CompilationReceipt Inspect(string manifestPath, bool requireCompiledBank)
    {
        Recovery recovery = Read(manifestPath);
        GraphicsDeviceType requiredApi = GraphicsApi(recovery);
        if (Application.unityVersion != "2021.3.5f1" || EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android
            || PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.Android)
            || !PlayerSettings.GetGraphicsAPIs(BuildTarget.Android).SequenceEqual(new[] { requiredApi }))
            throw new InvalidOperationException("Original compute gate requires Unity 2021.3.5f1 Android " + requiredApi + ".");
        var rows = new List<ShaderCompilation>();
        foreach (var contract in recovery.shaders)
        {
            if (contract.classId != 72 || contract.localFileId != 7200000 || contract.kernels == null
                || contract.kernels.Length != contract.kernelCount || !contract.assetPath.StartsWith("Assets/", StringComparison.Ordinal)
                || contract.assetPath.Contains("..") || Path.GetExtension(contract.assetPath) != ".compute"
                || Sha(contract.assetPath) != contract.sourceSha256 || Sha(contract.assetPath + ".meta") != contract.metaSha256)
                throw new InvalidDataException("Original compute source/identity contract differs: " + contract.name);
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(contract.assetPath);
            if (shader == null || shader.name != contract.name) throw new InvalidDataException("Compute source failed import: " + contract.assetPath);
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(shader, out string guid, out long localId)
                || guid != contract.guid || localId != contract.localFileId)
                throw new InvalidDataException("Original compute GUID/localID changed: " + contract.name);
            // Calling FindKernel/GetKernelThreadGroupSizes in a -nographics
            // editor asks Unity to compile the unsupported current Null GPU
            // renderer. Check source dispatch and native compiler bank metadata
            // without executing a device ComputeShader API in the build process.
            string sourceText = File.ReadAllText(contract.assetPath);
            var declaredKernels = Regex.Matches(sourceText, @"(?m)^#pragma kernel (\w+) QUEST_ORIGINAL_KERNEL_\d+$")
                .Cast<Match>().Select(match => match.Groups[1].Value).ToArray();
            var declaredGroups = Regex.Matches(sourceText, @"\[numthreads\((\d+), (\d+), (\d+)\)\]\s*void (\w+)\(")
                .Cast<Match>().ToArray();
            if (!declaredKernels.SequenceEqual(contract.kernels.Select(kernel => kernel.name))
                || declaredGroups.Length != contract.kernelCount)
                throw new InvalidDataException("Original compute kernel source order changed: " + contract.name);
            for (int index = 0; index < contract.kernels.Length; index++)
            {
                var kernel = contract.kernels[index];
                var groups = declaredGroups[index];
                if (groups.Groups[4].Value != kernel.name || kernel.threadGroups == null || kernel.threadGroups.Length != 3
                    || Enumerable.Range(0, 3).Any(axis => int.Parse(groups.Groups[axis + 1].Value) != kernel.threadGroups[axis]))
                    throw new InvalidDataException("Original compute kernel order/dispatch extent changed: " + contract.name + "/" + kernel.name);
            }
            var messages = ShaderUtil.GetComputeShaderMessages(shader);
            var errors = messages.Where(message => message.severity == ShaderCompilerMessageSeverity.Error).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException("Android compute compilation failed: " + contract.name + " "
                + string.Join(" | ", errors.Select(error => error.message).Take(8)));
            int count = requireCompiledBank ? Call<int>("GetComputeShaderPlatformCount", shader) : 0;
            var platforms = new List<string>();
            string[] compiledKernels = null;
            for (int platform = 0; platform < count; platform++)
            {
                var type = Call<GraphicsDeviceType>("GetComputeShaderPlatformType", shader, platform);
                platforms.Add(type.ToString());
                if (type != requiredApi) continue;
                if (compiledKernels != null) throw new InvalidDataException("Duplicate compute platform bank.");
                int kernels = Call<int>("GetComputeShaderPlatformKernelCount", shader, platform);
                compiledKernels = Enumerable.Range(0, kernels).Select(kernel =>
                    Call<string>("GetComputeShaderPlatformKernelName", shader, platform, kernel)).ToArray();
            }
            if (requireCompiledBank && (compiledKernels == null || !compiledKernels.SequenceEqual(contract.kernels.Select(kernel => kernel.name))))
                throw new InvalidDataException("Compiled compute bank does not contain every original kernel: " + contract.name);
            rows.Add(new ShaderCompilation { name = contract.name, assetPath = contract.assetPath, guid = guid, localFileId = localId,
                kernelCount = contract.kernelCount, platforms = platforms.ToArray(), compiledKernels = compiledKernels,
                compiledGlesKernels = requiredApi == GraphicsDeviceType.OpenGLES3 ? compiledKernels : null,
                compiledVulkanKernels = requiredApi == GraphicsDeviceType.Vulkan ? compiledKernels : null,
                warnings = messages.Where(message => message.severity != ShaderCompilerMessageSeverity.Error)
                    .Select(message => message.platform + ": " + message.message).Distinct().Take(16).ToArray() });
        }
        var receipt = new CompilationReceipt { shaderCount = rows.Count, kernelCount = rows.Sum(row => row.kernelCount),
            unityVersion = Application.unityVersion, buildTarget = BuildTarget.Android.ToString(), graphicsApi = requiredApi.ToString(),
            recoveryManifestSha256 = Sha(manifestPath), androidCompiled = requireCompiledBank, allOriginalKernelIdentitiesRetained = true,
            hardwareVerified = false, shaders = rows.ToArray() };
        return receipt;
    }

    /// <summary>Isolated tiny proof entry; never edits the original/full recovered project.</summary>
    public static void BuildProof()
    {
        string manifest = "QuestCampaignEvidence/compute-recovery.json";
        string output = Environment.GetEnvironmentVariable("GHVR_QUEST_COMPUTE_OUTPUT");
        if (string.IsNullOrEmpty(output)) throw new InvalidOperationException("GHVR_QUEST_COMPUTE_OUTPUT is required.");
        if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
            throw new InvalidOperationException("Android compute proof target switch failed.");
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        Recovery recovery = Read(manifest);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsApi(recovery) });
        foreach (var shader in recovery.shaders)
            AssetDatabase.ImportAsset(shader.assetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        Directory.CreateDirectory(output);
        var map = new[] { new AssetBundleBuild { assetBundleName = "quest-compute-proof.bundle",
            assetNames = recovery.shaders.Select(shader => shader.assetPath).ToArray() } };
        var built = BuildPipeline.BuildAssetBundles(output, map,
            BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle | BuildAssetBundleOptions.StrictMode,
            BuildTarget.Android);
        if (built == null || !built.GetAllAssetBundles().SequenceEqual(new[] { "quest-compute-proof.bundle" }))
            throw new InvalidOperationException("Actual Android compute proof bank failed.");
        var receipt = Validate(manifest, Path.Combine(output, "compute-compilation.json"));
        Debug.Log("Quest original compute Android " + receipt.graphicsApi + " proof: " + receipt.shaderCount + " shaders / " + receipt.kernelCount + " original kernels.");
    }
}
#endif
