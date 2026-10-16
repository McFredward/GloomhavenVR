using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class RegistrationRunner
{
    private static string result, evidence, assembly, method;
    private static IEnumerator run;
    private static bool started;
    public static void Start()
    {
        string[] args = Environment.GetCommandLineArgs();
        evidence = args[Array.IndexOf(args, "-registrationEvidence") + 1];
        assembly = args[Array.IndexOf(args, "-registrationAssembly") + 1];
        int methodIndex = Array.IndexOf(args, "-registrationMethod");
        method = methodIndex < 0 ? "Run" : args[methodIndex + 1];
        result = Path.Combine(evidence, "editor-result.json");
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        try
        {
            if (!started)
            {
                started = true;
                run = (IEnumerator)Assembly.LoadFile(assembly).GetType("RegistrationGpuFixture", true)
                    .GetMethod(method).Invoke(null, new object[] { evidence });
            }
            if (run.MoveNext()) { EditorApplication.QueuePlayerLoopUpdate(); return; }
            File.WriteAllText(result, "{\"passed\":true,\"xr_callbacks_executed\":false}\n");
            EditorApplication.update -= Tick;
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
            Debug.LogException(error);
            File.WriteAllText(result, "{\"passed\":false,\"reason\":\"" + Escape(error.ToString()) + "\"}\n");
            if (run is IDisposable disposable) disposable.Dispose();
            EditorApplication.update -= Tick;
            EditorApplication.Exit(1);
        }
    }
    private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"")
        .Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
}
