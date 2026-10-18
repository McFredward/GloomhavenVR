#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEditor;
using GloomhavenVR.Quest.Editor;
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
        public int schema = 1, shaderCount, kernelCount, targetedReimportedShaders;
        public string unityVersion, buildTarget, graphicsApi, recoveryManifestSha256;
        public bool androidCompiled, allOriginalKernelIdentitiesRetained, hardwareVerified;
        public ShaderCompilation[] shaders;
    }
    [Serializable] public sealed class ShaderCompilation
    {
        public string name, assetPath, guid;
        public long localFileId;
        public int kernelCount;
        public bool targetedReimported;
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
            || result.shaders.Any(shader => shader == null || string.IsNullOrEmpty(shader.name)
                || shader.kernels == null || shader.kernelCount != shader.kernels.Length
                || shader.kernels.Any(kernel => kernel == null || string.IsNullOrEmpty(kernel.name))
                || shader.kernels.Select(kernel => kernel.name).Distinct().Count() != shader.kernelCount)
            || result.shaders.Select(shader => shader.name).Distinct().Count() != 13
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
        // An unavailable Editor API is a toolchain error, not a stale asset.
        // Resolve it before offering file-level import repair.
        if (requireCompiledBank)
            foreach (string method in new[] { "GetComputeShaderPlatformCount", "GetComputeShaderPlatformType",
                "GetComputeShaderPlatformKernelCount", "GetComputeShaderPlatformKernelName" }) Api(method);
        var progress = new QuestWizardProgress.Counter("unity-compute-identities", requireCompiledBank ? "player" : "unity-validation", recovery.shaders.Length, "shaders", "Validate original compute identities and compiled metadata");
        foreach (var contract in recovery.shaders)
        {
            progress.Report(rows.Count, contract.assetPath);
            ValidateSource(contract, requireCompiledBank);
            ShaderCompilation row;
            try { row = ValidateImported(contract, requiredApi, requireCompiledBank); }
            catch (InvalidDataException first)
            {
                // Capture090432 failed on unchanged Windows CRLF source at the
                // first compute pragma. Source parsing must accept its native line
                // terminator, while keeping the complete byte SHA and every kernel
                // dispatch invariant. An independently stale Library object may
                // then be repaired by one exact-path synchronous import. Never
                // reset the Library, rewrite the original source/meta or retry an
                // unknown source change as if it were an import problem.
                ValidateSourceFiles(contract);
                Debug.Log("[Quest compute] targeted original ComputeShader reimport; asset=" + contract.assetPath
                    + "; reason=" + first.Message + "; attempt=1/1; original sources and Unity Library retained.");
                progress.Report(rows.Count, "Repairing imported ComputeShader: " + contract.assetPath);
                try { AssetDatabase.ImportAsset(contract.assetPath,
                    ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport); }
                catch (Exception failure)
                {
                    throw new InvalidDataException("Original compute targeted reimport failed: " + contract.assetPath
                        + "; attempt=1/1; " + failure.GetType().Name + ": " + failure.Message, failure);
                }
                // A repair can only complete after all original source, import
                // and (when requested) native Android bank checks pass again.
                ValidateSourceFiles(contract);
                try { row = ValidateImported(contract, requiredApi, requireCompiledBank); }
                catch (InvalidDataException second)
                {
                    throw new InvalidDataException(second.Message
                        + "; targeted reimport attempt=1/1 did not restore the original import/bank.", second);
                }
                row.targetedReimported = true;
                Debug.Log("[Quest compute] targeted original ComputeShader reimport verified; asset="
                    + contract.assetPath + "; attempt=1/1.");
            }
            rows.Add(row);
            progress.Report(rows.Count, contract.name);
        }
        progress.Complete("Original compute identities and native metadata validated");
        return new CompilationReceipt { shaderCount = rows.Count, kernelCount = rows.Sum(row => row.kernelCount),
            targetedReimportedShaders = rows.Count(row => row.targetedReimported),
            unityVersion = Application.unityVersion, buildTarget = BuildTarget.Android.ToString(), graphicsApi = requiredApi.ToString(),
            recoveryManifestSha256 = Sha(manifestPath), androidCompiled = requireCompiledBank, allOriginalKernelIdentitiesRetained = true,
            hardwareVerified = false, shaders = rows.ToArray() };
    }

    static void ValidateSourceFiles(ShaderContract contract)
    {
        if (contract.classId != 72 || contract.localFileId != 7200000 || string.IsNullOrEmpty(contract.assetPath)
            || !contract.assetPath.StartsWith("Assets/", StringComparison.Ordinal) || contract.assetPath.Contains("..")
            || contract.assetPath.Contains("\\") || Path.GetExtension(contract.assetPath) != ".compute")
            throw new InvalidDataException("Original compute source identity contract differs: " + contract.name + "; asset=" + contract.assetPath);
        RequireHash(contract.assetPath, contract.sourceSha256, "source");
        RequireHash(contract.assetPath + ".meta", contract.metaSha256, "metadata");
        var metaGuids = Regex.Matches(File.ReadAllText(contract.assetPath + ".meta"),
            @"(?m)^guid: ([0-9a-f]{32})\r?$");
        if (metaGuids.Count != 1 || metaGuids[0].Groups[1].Value != contract.guid)
            throw new InvalidDataException("Original compute source metadata GUID differs: " + contract.assetPath
                + "; expected=" + contract.guid + "; matching GUID declarations=" + metaGuids.Count);
    }

    static void ValidateSource(ShaderContract contract, bool requireCompiledBank)
    {
        ValidateSourceFiles(contract);
        // Calling FindKernel/GetKernelThreadGroupSizes in a -nographics
        // editor asks Unity to compile the unsupported current Null GPU
        // renderer. Check source dispatch and native compiler bank metadata
        // without executing a device ComputeShader API in the build process.
        string sourceText = File.ReadAllText(contract.assetPath);
        var declaredKernels = Regex.Matches(sourceText, @"(?m)^#pragma kernel (\w+) QUEST_ORIGINAL_KERNEL_(\d+)\r?$")
            .Cast<Match>().ToArray();
        var declaredDefines = Regex.Matches(sourceText, @"(?m)^#if defined\(QUEST_ORIGINAL_KERNEL_(\d+)\)\r?$")
            .Cast<Match>().ToArray();
        var declaredGroups = Regex.Matches(sourceText, @"\[numthreads\((\d+), (\d+), (\d+)\)\]\s*void (\w+)\(")
            .Cast<Match>().ToArray();
        if (declaredKernels.Length != contract.kernelCount || declaredDefines.Length != contract.kernelCount
            || declaredGroups.Length != contract.kernelCount)
            throw new InvalidDataException("Original compute kernel source census differs: " + contract.assetPath
                + "; expected=" + contract.kernelCount + "; pragmas=" + declaredKernels.Length
                + "; defines=" + declaredDefines.Length + "; dispatch functions=" + declaredGroups.Length);
        var kernelProgress = new QuestWizardProgress.Counter("unity-compute-kernels", requireCompiledBank ? "player" : "unity-validation", contract.kernels.Length, "kernels", contract.name);
        for (int index = 0; index < contract.kernels.Length; index++)
        {
            var kernel = contract.kernels[index];
            kernelProgress.Report(index, contract.name + "/" + kernel.name);
            var groups = declaredGroups[index];
            if (declaredKernels[index].Groups[1].Value != kernel.name
                || declaredKernels[index].Groups[2].Value != index.ToString()
                || declaredDefines[index].Groups[1].Value != index.ToString())
                throw new InvalidDataException("Original compute kernel source order/define differs: " + contract.assetPath
                    + "; index=" + index + "; expected=" + kernel.name + "/QUEST_ORIGINAL_KERNEL_" + index
                    + "; pragma=" + declaredKernels[index].Value + "; define=" + declaredDefines[index].Value);
            if (groups.Groups[4].Value != kernel.name || !MatchesThreadGroups(groups, kernel.threadGroups))
                throw new InvalidDataException("Original compute kernel order/dispatch extent changed: " + contract.assetPath + "/" + kernel.name
                    + "; expected=" + (kernel.threadGroups == null ? "missing" : string.Join(",", kernel.threadGroups))
                    + "; actual=" + string.Join(",", Enumerable.Range(1, 3).Select(axis => groups.Groups[axis].Value))
                    + "; dispatch function=" + groups.Groups[4].Value);
            kernelProgress.Report(index + 1, contract.name + "/" + kernel.name);
        }
        kernelProgress.Complete("Original compute dispatch extent retained: " + contract.name);
    }

    static bool MatchesThreadGroups(Match match, int[] expected)
    {
        if (expected == null || expected.Length != 3) return false;
        for (int axis = 0; axis < 3; axis++)
        {
            int actual;
            if (!int.TryParse(match.Groups[axis + 1].Value, out actual) || actual != expected[axis]) return false;
        }
        return true;
    }

    static void RequireHash(string path, string expected, string kind)
    {
        if (!File.Exists(path)) throw new InvalidDataException("Original compute " + kind + " is missing: " + path);
        string actual = Sha(path);
        if (actual != expected) throw new InvalidDataException("Original compute " + kind + " SHA differs: " + path
            + "; expected=" + expected + "; actual=" + actual + "; targeted import cannot repair unproved source bytes.");
    }

    static ShaderCompilation ValidateImported(ShaderContract contract, GraphicsDeviceType requiredApi, bool requireCompiledBank)
    {
        var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(contract.assetPath);
        if (shader == null) throw new InvalidDataException("Original compute imported object is missing: " + contract.assetPath);
        if (shader.name != contract.name) throw new InvalidDataException("Original compute imported name differs: " + contract.assetPath
            + "; expected=" + contract.name + "; actual=" + shader.name);
        if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(shader, out string guid, out long localId))
            throw new InvalidDataException("Original compute imported GUID/localID cannot be read: " + contract.assetPath);
        if (guid != contract.guid || localId != contract.localFileId)
            throw new InvalidDataException("Original compute imported GUID/localID differs: " + contract.assetPath
                + "; GUID expected=" + contract.guid + " actual=" + guid
                + "; localID expected=" + contract.localFileId + " actual=" + localId);
        var messages = ShaderUtil.GetComputeShaderMessages(shader);
        var errors = messages.Where(message => message.severity == ShaderCompilerMessageSeverity.Error).ToArray();
        if (errors.Length != 0) throw new InvalidDataException("Android compute compilation failed: " + contract.assetPath + " "
            + string.Join(" | ", errors.Select(error => error.message).Take(8)));
        int count = requireCompiledBank ? Call<int>("GetComputeShaderPlatformCount", shader) : 0;
        var platforms = new List<string>();
        string[] compiledKernels = null;
        for (int platform = 0; platform < count; platform++)
        {
            var type = Call<GraphicsDeviceType>("GetComputeShaderPlatformType", shader, platform);
            platforms.Add(type.ToString());
            if (type != requiredApi) continue;
            if (compiledKernels != null) throw new InvalidDataException("Duplicate compute platform bank: " + contract.assetPath + "; api=" + requiredApi);
            int kernels = Call<int>("GetComputeShaderPlatformKernelCount", shader, platform);
            compiledKernels = Enumerable.Range(0, kernels).Select(kernel =>
                Call<string>("GetComputeShaderPlatformKernelName", shader, platform, kernel)).ToArray();
        }
        if (requireCompiledBank && (compiledKernels == null || !compiledKernels.SequenceEqual(contract.kernels.Select(kernel => kernel.name))))
            throw new InvalidDataException("Compiled compute bank does not contain every original kernel: " + contract.assetPath
                + "; api=" + requiredApi + "; expected=" + string.Join(",", contract.kernels.Select(kernel => kernel.name))
                + "; actual=" + (compiledKernels == null ? "missing" : string.Join(",", compiledKernels)));
        return new ShaderCompilation { name = contract.name, assetPath = contract.assetPath, guid = guid, localFileId = localId,
            kernelCount = contract.kernelCount, platforms = platforms.ToArray(), compiledKernels = compiledKernels,
            compiledGlesKernels = requiredApi == GraphicsDeviceType.OpenGLES3 ? compiledKernels : null,
            compiledVulkanKernels = requiredApi == GraphicsDeviceType.Vulkan ? compiledKernels : null,
            warnings = messages.Where(message => message.severity != ShaderCompilerMessageSeverity.Error)
                .Select(message => message.platform + ": " + message.message).Distinct().Take(16).ToArray() };
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
