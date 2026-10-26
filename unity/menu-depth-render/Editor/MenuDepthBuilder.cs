using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class MenuDepthBuilder
{
    public static void Build()
    {
        try
        {
            string[] args = Environment.GetCommandLineArgs();
            string output = args[Array.IndexOf(args, "-menuDepthOutput") + 1];
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,
                new[] { GraphicsDeviceType.Direct3D11, GraphicsDeviceType.OpenGLCore, GraphicsDeviceType.Vulkan });
            AssetDatabase.Refresh();
            Directory.CreateDirectory(output);
            AssetBundleManifest result = BuildPipeline.BuildAssetBundles(output,
                new[] { new AssetBundleBuild { assetBundleName = "menu-depth.bundle", assetNames = new[]
                { "Assets/MenuDepth/MenuDepthDisplay.shader", "Assets/MenuDepth/MenuDepthGraphic.shader", "Assets/MenuDepth/Unity-LICENSE.txt" } } },
                BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64);
            if (result == null) throw new InvalidOperationException("Menu-depth shader bundle build failed.");
            File.WriteAllText(Path.Combine(output, "build-result.txt"), "Unity " + Application.unityVersion
                + "; Windows64; Direct3D11,OpenGLCore,Vulkan; TypeTrees enabled\n");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
}
