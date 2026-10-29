#nullable enable
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Compile exactly three original sky shaders; no scene or game import.</summary>
public static class DefaultSkyResourceBuilder
{
    [Serializable] private sealed class Variant { public string passType = ""; public string[] keywords = Array.Empty<string>(); }
    [Serializable] private sealed class Entry { public string assetPath = "", originalName = ""; public Variant[] variants = Array.Empty<Variant>(); }
    [Serializable] private sealed class Manifest { public Entry[] shaders = Array.Empty<Entry>(); }
    private static string Argument(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, name);
        if (index < 0 || index + 1 == args.Length || !Path.IsPathRooted(args[index + 1]))
            throw new InvalidOperationException("Missing absolute private " + name);
        return args[index + 1];
    }
    public static void Build()
    {
        try
        {
            if (Application.unityVersion != "2021.3.5f1" || EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                throw new InvalidOperationException("Native sky compilation requires Unity2021.3.5f1 Android.");
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.stereoRenderingPath = StereoRenderingPath.MultiPass;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Argument("-skyManifest")));
            string[] names = { "AMP_SkyShader", "Legacy Shaders/Diffuse", "Legacy Shaders/VertexLit" };
            if (manifest.shaders == null || manifest.shaders.Length != 3 ||
                !manifest.shaders.Select(s => s.originalName).OrderBy(s => s).SequenceEqual(names.OrderBy(s => s)))
                throw new InvalidDataException("Exactly the three original sky shaders are required.");
            AssetDatabase.Refresh();
            var variants = new ShaderVariantCollection();
            foreach (Entry entry in manifest.shaders)
            {
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(entry.assetPath);
                if (shader == null || shader.name != entry.originalName || entry.variants == null || entry.variants.Length == 0)
                    throw new InvalidDataException("Missing original sky shader or native variants: " + entry.assetPath);
                foreach (Variant variant in entry.variants)
                    variants.Add(new ShaderVariantCollection.ShaderVariant(shader,
                        (PassType)Enum.Parse(typeof(PassType), variant.passType), variant.keywords));
            }
            const string collection = "Assets/QuestDefaultSky/NativeSkyVariants.shadervariants";
            AssetDatabase.DeleteAsset(collection);
            AssetDatabase.CreateAsset(variants, collection);
            AssetDatabase.SaveAssets();
            string output = Argument("-skyOutput");
            Directory.CreateDirectory(output);
            var result = BuildPipeline.BuildAssetBundles(output, new[] { new AssetBundleBuild {
                assetBundleName = "native-sky-shaders.bundle",
                assetNames = manifest.shaders.Select(s => s.assetPath).Concat(new[] { collection }).ToArray() } },
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode |
                BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.Android);
            if (result == null) throw new InvalidOperationException("Three native sky shader compilation failed.");
            File.WriteAllText(Path.Combine(output, "build-result.txt"), "Unity2021.3.5f1; Android; Vulkan; MultiPass; exactly three original sky shaders\n");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    /// <summary>Exercise the actual runtime lookup contract, separately from byte parsing.</summary>
    public static void Probe()
    {
        AssetBundle? bank = null;
        try
        {
            if (Application.unityVersion != "2021.3.5f1" || SystemInfo.graphicsDeviceType != GraphicsDeviceType.Vulkan)
                throw new InvalidOperationException("Native sky readiness proof requires exact Unity and a Vulkan host.");
            bank = AssetBundle.LoadFromFile(Argument("-skyProbe"));
            if (bank == null) throw new InvalidDataException("Actual native sky AssetBundle.LoadFromFile failed.");
            string[] roots = { "native/default-sky/mesh", "native/default-sky/material", "native/default-sky/shader" };
            if (!bank.GetAllAssetNames().OrderBy(s => s).SequenceEqual(roots.OrderBy(s => s)))
                throw new InvalidDataException("Actual native sky loader root names differ.");
            Mesh mesh = bank.LoadAsset<Mesh>(roots[0]);
            Material material = bank.LoadAsset<Material>(roots[1]);
            if (mesh == null || mesh.name != "GH_SkySphere" || mesh.vertexCount != 1679 || material == null ||
                material.name != "BG_SkyClouds 2" || material.shader == null || material.shader.name != "AMP_SkyShader" || !material.shader.isSupported)
                throw new InvalidDataException("NativeDefaultSky.EnsureAssets lookup/support contract failed.");
            foreach (Texture2D? texture in new[] { material.GetTexture("_Map1") as Texture2D, material.GetTexture("_Map2") as Texture2D })
                if (texture == null || texture.mipmapCount < 10) throw new InvalidDataException("Native sky texture/mip dependency failed.");
            Shader[] shaders = bank.LoadAllAssets<Shader>();
            if (shaders.Length != 3 || shaders.Any(s => !s.isSupported))
                throw new InvalidDataException("Actual three-shader fallback support failed.");
            File.WriteAllText(Argument("-skyProbeResult"), "PASS actual LoadAsset<Mesh/Material>; original AMP shader supported; three shaders and complete texture dependencies\n");
            bank.Unload(true); bank = null;
            EditorApplication.Exit(0);
        }
        catch (Exception error) { if (bank != null) bank.Unload(true); Debug.LogException(error); EditorApplication.Exit(1); }
    }
}
