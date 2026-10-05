#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

public static class QuestShaderVulkanTrial
{
    public static void Dump()
    {
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
        string path = Environment.GetEnvironmentVariable("GHVR_VULKAN_TRIAL_SHADER") ?? "Assets/OriginalAmpForward.shader";
        string output = Environment.GetEnvironmentVariable("GHVR_VULKAN_TRIAL_OUTPUT") ?? "VulkanTrial";
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
        if (shader == null) throw new InvalidOperationException("Native Vulkan trial shader is absent.");
        Directory.CreateDirectory(output);
        var result = GloomhavenVR.Quest.Editor.QuestVulkanShaderValidation.Compile(shader, 0, 0, new string[0], GraphicsTier.Tier2);
        File.WriteAllBytes(Path.Combine(output, "Vertex.spv"), result.vertex);
        File.WriteAllBytes(Path.Combine(output, "Fragment.spv"), result.fragment);
        File.WriteAllText(Path.Combine(output, "reflection.json"), JsonUtility.ToJson(result, true));
        Debug.Log("PASS native Vulkan modules: " + result.vertex.Length + "/" + result.fragment.Length);
    }
}
#endif
