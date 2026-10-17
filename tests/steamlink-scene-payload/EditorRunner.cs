using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEngine;

// Direct Editor GPU execution: no play-mode transition, reflection or game scene.
public static class ScenePayloadEditorRunner
{
    private static IEnumerator routine;
    private static string output;
    public static void Start()
    {
        string[] args=Environment.GetCommandLineArgs();
        output=args[Array.IndexOf(args,"-sceneEvidence")+1];
        routine=RegistrationGpuFixture.Run(output);
        EditorApplication.update+=Step;
    }
    private static void Step()
    {
        try
        {
            if(routine.MoveNext())return;
            EditorApplication.update-=Step;
            EditorApplication.Exit(0);
        }
        catch(Exception error)
        {
            EditorApplication.update-=Step;
            File.WriteAllText(Path.Combine(output,"failure.txt"),error.ToString()+"\n");
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }
}
