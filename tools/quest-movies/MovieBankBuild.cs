using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
public static class MovieBankBuild
{
    public static void Build()
    {
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        foreach (string path in new[] { "Assets/StereoVideoUI.shader", "Assets/QuestMovieEye.shader" })
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidDataException("Movie shader import failed: " + path);
        }
        string output = Path.GetFullPath("../banks"); Directory.CreateDirectory(output);
        var build = new AssetBundleBuild { assetBundleName = "ghvr-quest-movies.bundle",
            assetNames = new[] { "Assets/StereoVideoUI.shader", "Assets/QuestMovieEye.shader" } };
        if (BuildPipeline.BuildAssetBundles(output, new[] { build }, BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.Android) == null)
            throw new InvalidDataException("Android movie shader bank build failed.");
        Debug.Log("GHVRQ_MOVIE_SHADERS_COMPLETE");
    }
}
