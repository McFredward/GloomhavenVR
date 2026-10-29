using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Compile only the current authored menu shaders and one owned native derivative.</summary>
public static class MenuDepthResourceBuilder
{
    public static void Build()
    {
        try
        {
            if (Application.unityVersion != "2021.3.5f1"
                || EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                throw new InvalidOperationException("Menu-depth resources require exact Unity2021.3.5f1 Android.");
            string[] args = Environment.GetCommandLineArgs();
            string output = args[Array.IndexOf(args, "-menuDepthOutput") + 1];
            if (!Path.IsPathRooted(output)) throw new InvalidOperationException("Absolute private output required.");
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.stereoRenderingPath = StereoRenderingPath.MultiPass;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            AssetDatabase.Refresh();
            string[] paths = { "Assets/MenuDepth/MenuDepthDisplay.shader", "Assets/MenuDepth/MenuDepthGraphic.shader",
                "Assets/MenuDepth/MenuDepthCharacter.shader", "Assets/MenuDepth/MenuDepthNativeCard.shader",
                "Assets/MenuDepth/MenuDepthHeadResolve.shader" };
            foreach (string path in paths)
                if (AssetDatabase.LoadAssetAtPath<Shader>(path) == null)
                    throw new InvalidOperationException("Missing menu-depth shader: " + path);
            // Shader_feature UI variants are selected by a borrowed material at
            // runtime. Explicit roots keep every original alpha/rect combination.
            var variants = new ShaderVariantCollection();
            Shader native = AssetDatabase.LoadAssetAtPath<Shader>("Assets/MenuDepth/MenuDepthNativeCard.shader");
            foreach (string[] keywords in new[] { new string[0], new[] { "UNITY_UI_ALPHACLIP" },
                new[] { "UNITY_UI_CLIP_RECT" }, new[] { "UNITY_UI_ALPHACLIP", "UNITY_UI_CLIP_RECT" } })
                variants.Add(new ShaderVariantCollection.ShaderVariant(native, PassType.Normal, keywords));
            const string collection = "Assets/MenuDepth/MenuDepthVariants.shadervariants";
            AssetDatabase.DeleteAsset(collection);
            AssetDatabase.CreateAsset(variants, collection);
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory(output);
            AssetBundleManifest result = BuildPipeline.BuildAssetBundles(output,
                new[] { new AssetBundleBuild { assetBundleName = "menu-depth.bundle",
                    assetNames = paths.Concat(new[] { collection, "Assets/MenuDepth/Unity-LICENSE.txt" }).ToArray() } },
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode
                    | BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.Android);
            if (result == null) throw new InvalidOperationException("Android menu-depth compilation failed.");
            File.WriteAllText(Path.Combine(output, "build-result.txt"),
                "Unity2021.3.5f1; Android; Vulkan; Linear; MultiPass; native card aliases retained\n");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
}
