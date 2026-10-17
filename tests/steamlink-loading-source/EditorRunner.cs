using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;

public static class LoadingSourceEditorRunner
{
    private static IEnumerator routine;
    private static string output;
    public static void Start()
    {
        string[] args=Environment.GetCommandLineArgs();
        output=args[Array.IndexOf(args,"-loadingEvidence")+1];
        var fixture=Assembly.Load("SteamLinkLoadingSourceFixture");
        routine=(IEnumerator)fixture.GetType("LoadingSourceFixture").GetMethod("Run").Invoke(null,new object[]{output,args[Array.IndexOf(args,"-sceneBank")+1]});
        EditorApplication.update+=Step;
    }
    private static void Step()
    {
        try
        {
            if(routine.MoveNext())return;
            EditorApplication.update-=Step;EditorApplication.Exit(0);
        }
        catch(Exception error)
        {
            EditorApplication.update-=Step;
            while(error is TargetInvocationException && error.InnerException!=null)error=error.InnerException;
            File.WriteAllText(Path.Combine(output,"failure.txt"),error.ToString()+"\n");
            UnityEngine.Debug.LogException(error);EditorApplication.Exit(1);
        }
    }
}
