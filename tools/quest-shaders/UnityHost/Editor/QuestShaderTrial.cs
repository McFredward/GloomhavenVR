#if UNITY_EDITOR
using System;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

public static class QuestShaderTrial
{
    public static void Bundle()
    {
        var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/OriginalAmpForward.shader");
        if (shader == null) throw new InvalidOperationException("Original bound shader trial is missing.");
        var material = new Material(shader);
        if (AssetDatabase.LoadAssetAtPath<Material>("Assets/TrialMaterial.mat") != null) AssetDatabase.DeleteAsset("Assets/TrialMaterial.mat");
        AssetDatabase.CreateAsset(material, "Assets/TrialMaterial.mat");
        var mesh = new Mesh {
            vertices = new[] { new Vector3(-0.7f, -0.7f, 0), new Vector3(0, 0.7f, 0), new Vector3(0.7f, -0.7f, 0) },
            normals = new[] { new Vector3(0, 0.6f, -0.8f), new Vector3(0, 0.6f, -0.8f), new Vector3(0, 0.6f, -0.8f) },
            tangents = new[] { new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1) },
            uv = new[] { Vector2.zero, new Vector2(0.5f, 1), Vector2.right }, triangles = new[] { 0, 1, 2 }
        };
        mesh.RecalculateBounds();
        if (AssetDatabase.LoadAssetAtPath<Mesh>("Assets/TrialMesh.asset") != null) AssetDatabase.DeleteAsset("Assets/TrialMesh.asset");
        AssetDatabase.CreateAsset(mesh, "Assets/TrialMesh.asset");
        AssetDatabase.SaveAssets();
        bool vulkan = Environment.GetEnvironmentVariable("GHVR_QUEST_SHADER_VULKAN_HOST") == "1";
        var target = vulkan ? BuildTarget.StandaloneLinux64 : BuildTarget.StandaloneWindows64;
        PlayerSettings.SetUseDefaultGraphicsAPIs(target, false);
        PlayerSettings.SetGraphicsAPIs(target, new[] { vulkan ? GraphicsDeviceType.Vulkan : GraphicsDeviceType.Direct3D11 });
        Directory.CreateDirectory(vulkan ? "TrialVulkan" : "TrialWindows");
        var result = BuildPipeline.BuildAssetBundles(vulkan ? "TrialVulkan" : "TrialWindows", new[] { new AssetBundleBuild {
            assetBundleName = "quest-campaign-shader-candidates", assetNames = new[] { "Assets/TrialMaterial.mat", "Assets/TrialMesh.asset" },
            addressableNames = new[] { "candidate-material", "candidate-mesh" }
        } }, BuildAssetBundleOptions.StrictMode | BuildAssetBundleOptions.ForceRebuildAssetBundle, vulkan ? BuildTarget.StandaloneLinux64 : BuildTarget.StandaloneWindows64);
        if (result == null) throw new InvalidOperationException("Actual bound original forward native bundle failed.");
    }
    [Serializable] public sealed class Receipt
    {
        public int schema = 1;
        public string unityVersion, glesSha256;
        public bool androidVertexCompiled, androidFragmentCompiled, originalPixelParityVerified, headsetPictureVerified;
    }

    public static void Compile()
    {
        var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/OriginalAmpForward.shader");
        if (shader == null) throw new InvalidOperationException("Original bound shader trial is missing.");
        foreach (var message in ShaderUtil.GetShaderMessages(shader))
            if (message.severity == ShaderCompilerMessageSeverity.Error)
                throw new InvalidOperationException("Original shader import error: " + message.message);
        var pass = ShaderUtil.GetShaderData(shader).GetSubshader(0).GetPass(0);
        var vertex = pass.CompileVariant(ShaderType.Vertex, new string[0], ShaderCompilerPlatform.GLES3x, BuildTarget.Android);
        // GLES returns both native stages from the Vertex query. The Fragment
        // query is empty by contract; treating that as failure misreads Unity.
        if (!vertex.Success || vertex.ShaderData == null || vertex.ShaderData.Length == 0)
            throw new InvalidOperationException("Actual bound original forward GLES compile failed.");
        string source = System.Text.Encoding.UTF8.GetString(vertex.ShaderData);
        if (!source.Contains("#ifdef VERTEX") || !source.Contains("#ifdef FRAGMENT"))
            throw new InvalidOperationException("Actual bound original GLES output lacks both native stages.");
        Directory.CreateDirectory("TrialOutput");
        File.WriteAllBytes("TrialOutput/vertex.glsl", vertex.ShaderData);
        File.WriteAllBytes("TrialOutput/combined.glsl", vertex.ShaderData);
        string hash;
        using (var algorithm = SHA256.Create()) hash = BitConverter.ToString(algorithm.ComputeHash(vertex.ShaderData)).Replace("-", "").ToLowerInvariant();
        File.WriteAllText("TrialOutput/results.json", JsonUtility.ToJson(new Receipt {
            unityVersion = Application.unityVersion, androidVertexCompiled = true, androidFragmentCompiled = true, glesSha256 = hash
        }, true) + "\n");
        Debug.Log("PASS original bound Amp forward Android GLES compile.");
    }
}
#endif
