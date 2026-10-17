using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class SceneShaderBuilder
{
    public static void Build()
    {
        try
        {
            string[] args = Environment.GetCommandLineArgs();
            string output = args[Array.IndexOf(args, "-sceneBundleOutput") + 1];
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,
                new[] { GraphicsDeviceType.Direct3D11, GraphicsDeviceType.OpenGLCore, GraphicsDeviceType.Vulkan });
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            string[] shaders = Directory.GetFiles("Assets/SteamLink", "*.shader", SearchOption.AllDirectories);
            Array.Sort(shaders, StringComparer.Ordinal);
            if (shaders.Length < 2) throw new InvalidDataException("Scene shader sources are missing.");
            Directory.CreateDirectory(output);
            var manifest = BuildPipeline.BuildAssetBundles(output,
                new[] { new AssetBundleBuild { assetBundleName = "scene.bundle", assetNames = shaders } },
                BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64);
            if (manifest == null) throw new InvalidDataException("Scene shader bank failed to build.");
            File.WriteAllText(Path.Combine(output, "build-result.txt"), Application.unityVersion + "; "
                + shaders.Length + " owned shaders; Windows64 D3D11/OpenGLCore/Vulkan; TypeTrees enabled\n");
            EditorApplication.Exit(0);
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }
}
