using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class Builder
{
    public static void BuildLinux()
    {
        try
        {
            Directory.CreateDirectory("Assets/Scenes");
            const string scenePath = "Assets/Scenes/Empty.unity";
            if (!File.Exists(scenePath))
            {
                Scene s = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(s, scenePath);
            }

            PlayerSettings.runInBackground = true;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);

            var opts = new BuildPlayerOptions
            {
                scenes = new[] { scenePath },
                locationPathName = "Build/clothbench",
                target = BuildTarget.StandaloneLinux64,
                options = BuildOptions.Development,
            };
            BuildReport report = BuildPipeline.BuildPlayer(opts);
            Debug.Log("[BUILD] result " + report.summary.result + " errors " + report.summary.totalErrors);
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }
        catch (Exception e)
        {
            Debug.LogError("[BUILD] threw " + e);
            EditorApplication.Exit(2);
        }
    }
}
