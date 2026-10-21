using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class QuestLoadingShaderBuilder
{
    public static void Build()
    {
        try
        {
            if (Application.unityVersion != "2021.3.5f1") throw new InvalidOperationException("Exact game editor required.");
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android) throw new InvalidOperationException("Android target required.");
            string[] args = Environment.GetCommandLineArgs();
            string output = args[Array.IndexOf(args, "-loadingBundleOutput") + 1];
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.stereoRenderingPath = StereoRenderingPath.MultiPass;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            const string source = "Assets/QuestLoading/QuestLoadingSpinner.shader";
            if (AssetDatabase.LoadAssetAtPath<Shader>(source) == null) throw new InvalidDataException("Owned loading shader missing.");
            Directory.CreateDirectory(output);
            var manifest = BuildPipeline.BuildAssetBundles(output,
                new[] { new AssetBundleBuild { assetBundleName = "ghvr-quest-loading.bundle", assetNames = new[] { source } } },
                BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.Android);
            if (manifest == null) throw new InvalidDataException("Owned loading shader bundle compilation failed.");
            File.WriteAllText(Path.Combine(output, "build-result.txt"), "2021.3.5f1; Android; Vulkan; Linear; TypeTrees enabled; LZ4\n");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
}
