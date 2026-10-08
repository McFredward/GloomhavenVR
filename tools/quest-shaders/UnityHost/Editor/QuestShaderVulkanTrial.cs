#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

public static class QuestShaderVulkanTrial
{
    public static void BuildPixels()
    {
        Environment.SetEnvironmentVariable("GHVR_QUEST_SHADER_VULKAN_HOST", "1");
        QuestShaderTrial.Bundle();
        QuestShaderHostBuild.Build();
    }

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
        GloomhavenVR.Quest.Editor.QuestVulkanShaderValidation.RequireColorOutput(result);
        GloomhavenVR.Quest.Editor.QuestVulkanShaderValidation.RequirePlain2D(result, "_Diffuse");
        int rejected = 0;
        foreach (var bad in new[] { new byte[51], Truncated(result.bank), BadStageExtent(result.bank) })
        {
            try { GloomhavenVR.Quest.Editor.QuestVulkanShaderValidation.Decode(bad); }
            catch (InvalidOperationException) { rejected++; }
        }
        result.images[0].arrayed = true;
        try { GloomhavenVR.Quest.Editor.QuestVulkanShaderValidation.RequirePlain2D(result, result.images[0].name); }
        catch (InvalidOperationException) { rejected++; }
        if (rejected != 4) throw new InvalidOperationException("Vulkan native negative control was accepted.");
        File.WriteAllText(Path.Combine(output, "negative-controls.json"), "{\"truncatedTableRejected\":true,\"truncatedPayloadRejected\":true,\"invalidExtentRejected\":true,\"unexpectedArrayRejected\":true}\n");
        Debug.Log("PASS native Vulkan modules: " + result.vertex.Length + "/" + result.fragment.Length + "; negative controls=" + rejected);
    }
    private static byte[] Truncated(byte[] input) { var bytes = new byte[input.Length - 1]; Array.Copy(input, bytes, bytes.Length); return bytes; }
    private static byte[] BadStageExtent(byte[] input) { var bytes = (byte[])input.Clone(); Array.Copy(BitConverter.GetBytes(uint.MaxValue), 0, bytes, 8, 4); return bytes; }

}
#endif
