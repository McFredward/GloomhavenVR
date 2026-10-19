using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;

public static class MenuRigEditorRunner
{
    private static IEnumerator routine;
    private static string output;
    public static void Start()
    {
        string[] args=Environment.GetCommandLineArgs();
        output=args[Array.IndexOf(args,"-loadingEvidence")+1];
        var fixture=Assembly.Load("MenuRigLifecycleFixture");
        routine=(IEnumerator)fixture.GetType("MenuRigLifecycleFixture").GetMethod("Run").Invoke(null,new object[]{output,args[Array.IndexOf(args,"-sceneBank")+1]});
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        EditorApplication.update += Begin;
        EditorApplication.EnterPlaymode();
    }
    private static void Begin()
    {
        if (!EditorApplication.isPlaying) return;
        EditorApplication.update -= Begin;
        new UnityEngine.GameObject("menu fixture coroutine").AddComponent<MenuRigCoroutine>().StartCoroutine(Step());
    }
    private static IEnumerator Step()
    {
        while (true)
        {
            bool next = false;
            try { next = routine.MoveNext(); }
            catch(Exception error)
            {
                while(error is TargetInvocationException && error.InnerException!=null)error=error.InnerException;
                File.WriteAllText(Path.Combine(output,"failure.txt"),error.ToString()+"\n");
                UnityEngine.Debug.LogException(error);EditorApplication.Exit(1); yield break;
            }
            if (!next) { EditorApplication.Exit(0); yield break; }
            yield return routine.Current;
        }
    }
}
public sealed class MenuRigCoroutine : UnityEngine.MonoBehaviour { }
