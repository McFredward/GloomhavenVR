#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class QuestShaderHostBuild
{
    public static void Build()
    {
        if (Application.unityVersion != "2021.3.5f1") throw new InvalidOperationException("Native original shader host requires Unity 2021.3.5f1.");
        Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/ShaderReference.unity");
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
        bool vulkan = Environment.GetEnvironmentVariable("GHVR_QUEST_SHADER_VULKAN_HOST") == "1";
        if (vulkan) PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneLinux64, new[] { GraphicsDeviceType.Vulkan });
        else PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
        PlayerSettings.colorSpace = Environment.GetEnvironmentVariable("GHVR_QUEST_SHADER_COLOR_SPACE") == "Gamma" ? ColorSpace.Gamma : ColorSpace.Linear;
        PlayerSettings.runInBackground = true;
        PlayerSettings.defaultScreenWidth = 512;
        PlayerSettings.defaultScreenHeight = 512;
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[] { "Assets/Scenes/ShaderReference.unity" }, locationPathName = vulkan ? "Linux/QuestShaderReference" : "Windows/QuestShaderReference.exe",
            target = vulkan ? BuildTarget.StandaloneLinux64 : BuildTarget.StandaloneWindows64, options = BuildOptions.Development | BuildOptions.StrictMode
        });
        if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Native original shader reference host failed: " + report.summary.result);
    }
}
#endif
