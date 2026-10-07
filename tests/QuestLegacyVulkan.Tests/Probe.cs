#if UNITY_EDITOR && GHVR_QUEST_GAME
using System;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Quest.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Actual narrow Vulkan compiler gates and their source/routing boundaries.</summary>
public static class QuestLegacyVulkanProbe
{
    [Serializable] public sealed class Result
    {
        public int schema = 1, actualBanks, actualStages, defectControls;
        public string unityVersion, compilerPlatform = "Vulkan";
        public bool hardwareVerified, androidAssetsBuilt;
        public string[] cases;
    }
    static readonly List<string> Cases = new List<string>();

    public static void Run()
    {
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
        PlayerSettings.stereoRenderingPath = StereoRenderingPath.MultiPass;
        Directory.CreateDirectory("ProbeOutput");
        Directory.CreateDirectory("ProbeBundles");
        var bundle = BuildPipeline.BuildAssetBundles("ProbeBundles", new[] {
            new AssetBundleBuild { assetBundleName = "audited-legacy-shaders", assetNames = new[] {
                "Assets/Shader/Hidden_BlendForBloom.shader", "Assets/Shader/Hidden_BrightPassFilter2.shader",
                "Assets/Shader/Hidden_BlurAndFlares.shader", "Assets/Shader/Splash Screen Shader.shader",
                "Assets/Shader/UI_Dissolve mask.shader", "Assets/Shader/Custom_SimpleGrabPassBlur.shader",
                QuestVideoValidation.SourcePath, QuestWorldScreenValidation.SourcePath, QuestWorldScreenValidation.GlassSourcePath
            } }
        }, BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.Android);
        Require(bundle != null && File.Exists("ProbeBundles/audited-legacy-shaders") &&
            new FileInfo("ProbeBundles/audited-legacy-shaders").Length > 0, "actual-android-assetbundle-built");
        QuestPostEffectValidation.Validate(true);
        QuestUiAssetValidation.Validate(true);
        QuestVideoValidation.Validate(true);
        QuestWorldScreenValidation.Validate(true);
        var bloom = JsonUtility.FromJson<QuestPostEffectValidation.ValidationReceipt>(File.ReadAllText(QuestPostEffectValidation.ReceiptPath));
        var ui = JsonUtility.FromJson<QuestUiAssetValidation.ValidationReceipt>(File.ReadAllText(QuestUiAssetValidation.ReceiptPath));
        var blur = JsonUtility.FromJson<QuestBlurValidation.ValidationReceipt>(File.ReadAllText(QuestBlurValidation.ReceiptPath));
        var video = JsonUtility.FromJson<QuestVideoValidation.Receipt>(File.ReadAllText(QuestVideoValidation.ReceiptPath));
        var world = JsonUtility.FromJson<QuestWorldScreenValidation.Receipt>(File.ReadAllText(QuestWorldScreenValidation.ReceiptPath));
        Require(bloom.compilerPlatform == "Vulkan" && bloom.allVulkanPassStagesCompiled && !bloom.allGlesPassStagesCompiled &&
            bloom.combinedProgramCount == 18 && bloom.compiledStageSections == 36, "bloom:18-native-banks");
        Require(ui.compilerPlatform == "Vulkan" && ui.allVulkanPassStagesCompiled && !ui.allGlesPassStagesCompiled &&
            ui.vulkanPrograms.Length == 3 && ui.glesPrograms.Length == 0, "ui:3-native-banks");
        Require(blur.compilerPlatform == "Vulkan" && blur.allVulkanPassStagesCompiled && !blur.allGlesPassStagesCompiled &&
            blur.vulkanPrograms.Length == 4 && blur.glesPrograms.Length == 0, "blur:4-native-banks");
        Require(video.compilerPlatform == "Vulkan" && video.allVulkanPassStagesCompiled && !video.allGlesPassStagesCompiled &&
            video.compiledStages == 2 && video.nativePlaybackPreserved, "camera:1-native-bank");
        Require(world.compilerPlatform == "Vulkan" && world.allVulkanPassStagesCompiled && !world.allGlesPassStagesCompiled &&
            world.compiledStages == 2 && world.banks.Length == 1 && !world.banks[0].stereoEyeRouting &&
            world.banks[0].plainCaptureSamplers && world.multiPassTextureRouting && world.stereoRenderingPath == "MultiPass",
            "world:1-native-multipass-bank");
        Require(world.glassCompiledStages == 2 && world.glassBanks.Length == 1
            && world.glassBanks[0].plainCaptureSamplers && !world.glassBanks[0].stereoEyeRouting
            && world.glassSourceSha256.Length == 64, "glass:1-native-premultiplied-bank");
        // Nine narrow shaders, with twenty-eight actual keyword/pass banks.
        Require(!world.hardwareVisualsVerified && !video.hardwareVisualsVerified && !ui.originalPixelParityVerified &&
            !blur.originalPixelParityVerified && !bloom.originalPixelParityVerified, "evidence-scope");
        Mutate("Assets/Shader/Hidden_BlendForBloom.shader", "// changed source\n", () => QuestPostEffectValidation.Validate(true), "bloom-source-sha");
        Mutate("Assets/Shader/Splash Screen Shader.shader", "// changed source\n", () => QuestUiAssetValidation.Validate(true), "ui-source-sha");
        Mutate("Assets/Shader/Custom_SimpleGrabPassBlur.shader", "// changed source\n", () => QuestBlurValidation.Validate(true), "blur-source-sha");
        Mutate(QuestVideoValidation.SourcePath, "// changed source\n", () => QuestVideoValidation.Validate(true), "video-source-sha");
        PlayerSettings.stereoRenderingPath = StereoRenderingPath.SinglePass;
        Reject(() => QuestWorldScreenValidation.Validate(true), "world-unproven-singlepass");
        PlayerSettings.stereoRenderingPath = StereoRenderingPath.MultiPass;
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
        Reject(() => QuestVideoValidation.Validate(true), "wrong-backend");
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(QuestWorldScreenValidation.SourcePath);
        var reflected = QuestVulkanShaderValidation.Compile(shader, 0, 0, new string[0], GraphicsTier.Tier2);
        Reject(() => QuestVulkanShaderValidation.RequirePlain2D(reflected, "_AbsentCapture"), "missing-reflected-capture");
        // Restore receipts after each deliberately failing production invocation.
        QuestPostEffectValidation.Validate(true);
        QuestUiAssetValidation.Validate(true);
        QuestVideoValidation.Validate(true);
        QuestWorldScreenValidation.Validate(true);
        File.WriteAllText("ProbeOutput/results.json", JsonUtility.ToJson(new Result {
            actualBanks = bloom.combinedProgramCount + ui.vulkanPrograms.Length + blur.vulkanPrograms.Length
                + 1 + world.banks.Length + world.glassBanks.Length,
            actualStages = bloom.compiledStageSections + ui.vulkanPrograms.Length * 2 + blur.vulkanPrograms.Length * 2
                + video.compiledStages + world.compiledStages + world.glassCompiledStages, defectControls = 7,
            unityVersion = Application.unityVersion, hardwareVerified = false, androidAssetsBuilt = true, cases = Cases.ToArray()
        }, true) + "\n");
    }
    static void Require(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("Native Vulkan fixture evidence differs: " + name);
        Cases.Add(name);
    }
    static void Reject(Action action, string name)
    {
        try { action(); }
        catch (InvalidOperationException) { Cases.Add(name + ":rejected"); return; }
        throw new InvalidOperationException("Native Vulkan production gate accepted defect: " + name);
    }
    static void Mutate(string path, string suffix, Action action, string name)
    {
        byte[] original = File.ReadAllBytes(path);
        try { File.AppendAllText(path, suffix); Reject(action, name); }
        finally { File.WriteAllBytes(path, original); }
    }
}
#endif
