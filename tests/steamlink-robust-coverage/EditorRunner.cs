using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class RobustSourceEditorRunner
{
    public static void Start()
    {
        string[] args=Environment.GetCommandLineArgs();
        string output=args[Array.IndexOf(args,"-robustOutput")+1];
        try { RobustSourceFixture.Run(output,args[Array.IndexOf(args,"-sceneBank")+1]);EditorApplication.Exit(0); }
        catch(Exception error)
        {
            File.WriteAllText(Path.Combine(output,"failure.txt"),error+"\n");
            Debug.LogException(error);EditorApplication.Exit(1);
        }
    }
}
