using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class QuestProceduralHostBuild
{
    public static void Build()
    {
        string destination = Environment.GetEnvironmentVariable("GHVR_PROCEDURAL_HOST_EXE");
        if (string.IsNullOrEmpty(destination)) throw new InvalidOperationException("Native export host output is missing.");
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
        PlayerSettings.SetApiCompatibilityLevel(BuildTargetGroup.Standalone, ApiCompatibilityLevel.NET_4_6);
        PlayerSettings.runInBackground = true;
        PlayerSettings.companyName = "GloomhavenVR";
        PlayerSettings.productName = "GloomhavenVR original procedural export host";
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("Original procedural host").AddComponent<QuestProceduralHostProbe>();
        Directory.CreateDirectory("Assets/Scenes");
        const string path = "Assets/Scenes/ProceduralHost.unity";
        EditorSceneManager.SaveScene(scene, path);
        Directory.CreateDirectory(Path.GetDirectoryName(destination));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { path }, locationPathName = destination,
            target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException("Original procedural export host failed: " + report.summary.result);
    }
}
