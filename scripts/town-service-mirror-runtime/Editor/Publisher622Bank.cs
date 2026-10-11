using System;
using System.IO;
using UnityEditor;

public static class Publisher622Bank
{
    public static void Build()
    {
        string[] args = Environment.GetCommandLineArgs();
        string output = args[Array.IndexOf(args, "-publisher622BankOutput") + 1];
        Directory.CreateDirectory(output);
        PlayerSettings.stripEngineCode = false;
        PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Standalone, ManagedStrippingLevel.Disabled);
        string[] paths = {
            "Assets/Bundle/TownServices/Shaders/TownNpc.shader",
            "Assets/Bundle/TownServices/Shaders/TownEye.shader",
            "Assets/Bundle/TownServices/Shaders/TownCornea.shader",
            "Assets/Bundle/TownServices/Shaders/TownFlame.shader"
        };
        var result = BuildPipeline.BuildAssetBundles(output,
            new[] { new AssetBundleBuild { assetBundleName = "publisher622-town.bundle", assetNames = paths } },
            BuildAssetBundleOptions.ForceRebuildAssetBundle | BuildAssetBundleOptions.UncompressedAssetBundle,
            BuildTarget.StandaloneLinux64);
        if (result == null) throw new InvalidOperationException("Original town shader bank build failed.");
        EditorApplication.Exit(0);
    }
}
