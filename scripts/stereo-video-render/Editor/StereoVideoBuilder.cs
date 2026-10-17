using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class StereoVideoBuilder
{
    public static void Build()
    {
        try
        {
            string[] args = Environment.GetCommandLineArgs();
            string manifestPath = args[Array.IndexOf(args, "-stereoBuildManifest") + 1];
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath));
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,
                new[] { GraphicsDeviceType.Direct3D11, GraphicsDeviceType.OpenGLCore, GraphicsDeviceType.Vulkan });
            Directory.CreateDirectory("Assets/StereoVideo");
            File.Copy(manifest.license, "Assets/StereoVideo/Unity-LICENSE.txt", true);
            AssetDatabase.ImportAsset("Assets/StereoVideo/Unity-LICENSE.txt", ImportAssetOptions.ForceUpdate);
            foreach (var entry in manifest.cases)
            {
                File.Copy(entry.shader, "Assets/StereoVideo/StereoVideoUI.shader", true);
                AssetDatabase.ImportAsset("Assets/StereoVideo/StereoVideoUI.shader", ImportAssetOptions.ForceUpdate);
                Directory.CreateDirectory(entry.output);
                var result = BuildPipeline.BuildAssetBundles(entry.output,
                    new[] { new AssetBundleBuild { assetBundleName = entry.name + ".bundle",
                        assetNames = new[] { "Assets/StereoVideo/StereoVideoUI.shader", "Assets/StereoVideo/Unity-LICENSE.txt" } } },
                    BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64);
                if (result == null) throw new Exception("Stereo shader bundle build failed: " + entry.name);
            }
            File.WriteAllText(manifest.result, "Unity " + Application.unityVersion
                + "; Windows64; Direct3D11,OpenGLCore,Vulkan; TypeTrees enabled\n");
            EditorApplication.Exit(0);
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }
    [Serializable] private class Entry { public string name, shader, output; }
    [Serializable] private class Manifest { public string result, license; public Entry[] cases; }
}
