#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
public static class QuestShaderInstanceNaNTrial
{
    public static void Build()
    {
        bool vulkan=Environment.GetEnvironmentVariable("GHVR_QUEST_SHADER_VULKAN_HOST")=="1";
        var target=vulkan?BuildTarget.StandaloneLinux64:BuildTarget.StandaloneWindows64;
        PlayerSettings.SetUseDefaultGraphicsAPIs(target,false);
        PlayerSettings.SetGraphicsAPIs(target,new[]{vulkan?GraphicsDeviceType.Vulkan:GraphicsDeviceType.Direct3D11});
        var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/InstanceLow.shader");
        if(shader==null)throw new InvalidOperationException("Actual original Amp_Low candidate is absent.");
        if(AssetDatabase.LoadAssetAtPath<Material>("Assets/InstanceLow.mat")!=null)AssetDatabase.DeleteAsset("Assets/InstanceLow.mat");
        var material=new Material(shader){enableInstancing=true,shaderKeywords=QuestShaderInstanceNaNOracle.Keywords};
        AssetDatabase.CreateAsset(material,"Assets/InstanceLow.mat");AssetDatabase.SaveAssets();
        string output=vulkan?"InstanceVulkan":"InstanceWindows";Directory.CreateDirectory(output);
        var bank=BuildPipeline.BuildAssetBundles(output,new[]{new AssetBundleBuild{assetBundleName="instance-nan-witness",assetNames=new[]{"Assets/InstanceLow.shader","Assets/InstanceLow.mat"},addressableNames=new[]{QuestShaderInstanceNaNOracle.Address,"candidate-material"}}},BuildAssetBundleOptions.StrictMode|BuildAssetBundleOptions.ForceRebuildAssetBundle,target);
        if(bank==null)throw new InvalidOperationException("Actual original instanced Amp_Low bank failed.");
        QuestShaderHostBuild.Build();
    }
}
#endif
