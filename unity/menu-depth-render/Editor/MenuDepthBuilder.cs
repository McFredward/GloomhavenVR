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
            PlayerSettings.colorSpace = ColorSpace.Gamma; // Original game's active colour space.
            AssetDatabase.Refresh();
            Directory.CreateDirectory(output);
            AssetBundleManifest result = BuildPipeline.BuildAssetBundles(output,
                new[] { new AssetBundleBuild { assetBundleName = "menu-depth.bundle", assetNames = new[]
                { "Assets/MenuDepth/MenuDepthDisplay.shader", "Assets/MenuDepth/MenuDepthGraphic.shader", "Assets/MenuDepth/Unity-LICENSE.txt" } } },
                BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64);
            if (result == null) throw new InvalidOperationException("Menu-depth shader bundle build failed.");
            AssetBundle probe = AssetBundle.LoadFromMemory(File.ReadAllBytes(Path.Combine(output, "menu-depth.bundle")));
            if (probe == null) throw new InvalidDataException("The built menu-depth archive cannot load in the exact Unity runtime.");
            try
            {
                Shader display = probe.LoadAsset<Shader>("assets/menudepth/menudepthdisplay.shader");
                Shader graphic = probe.LoadAsset<Shader>("assets/menudepth/menudepthgraphic.shader");
                if (probe.GetAllAssetNames().Length != 3 || display == null || graphic == null || !display.isSupported || !graphic.isSupported)
                    throw new InvalidDataException("The built menu-depth archive is missing supported runtime shader assets.");
                File.WriteAllText(Path.Combine(output, "runtime-load.txt"), "Exact Unity " + Application.unityVersion
                    + "; LoadFromMemory; three assets; display and graphic shaders supported on " + SystemInfo.graphicsDeviceType + "\n");
            }
            finally { probe.Unload(true); }
            File.WriteAllText(Path.Combine(output, "build-result.txt"), "Unity " + Application.unityVersion
                + "; Windows64; Direct3D11,OpenGLCore,Vulkan; TypeTrees enabled\n");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
}
