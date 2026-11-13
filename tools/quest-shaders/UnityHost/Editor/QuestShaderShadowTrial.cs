#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
public static class QuestShaderShadowTrial
{
    public static void Build()
    {
        bool vulkan=Environment.GetEnvironmentVariable("GHVR_QUEST_SHADER_VULKAN_HOST")=="1";
        string output=vulkan?"ShadowVulkan":"ShadowWindows";Directory.CreateDirectory(output);
        var result=BuildPipeline.BuildAssetBundles(output,new[]{new AssetBundleBuild{assetBundleName="shadow-witness",assetNames=new[]{"Assets/ShadowAmp.shader","Assets/DepthCopy.shader"},addressableNames=new[]{"Assets/Content/Characters/Common/Shaders/Amp_CharShader.shader","depth-copy"}}},BuildAssetBundleOptions.StrictMode|BuildAssetBundleOptions.ForceRebuildAssetBundle,vulkan?BuildTarget.StandaloneLinux64:BuildTarget.StandaloneWindows64);
        if(result==null)throw new InvalidOperationException("Native shadow witness bank failed.");
        QuestShaderHostBuild.Build();
    }
}
#endif
