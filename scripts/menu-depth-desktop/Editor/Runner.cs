using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class MenuDepthDesktopRunner
{
    [Serializable] private sealed class Case { public string name, dll, expected; }
    [Serializable] private sealed class Manifest { public string result, evidence; public Case[] cases; }
    private static Manifest manifest;
    private static StreamWriter writer;
    private static IEnumerator routine;
    private static int index;
    private static int assertions;
    private static bool pass = true;
    public static void Start()
    {
        string[] args = Environment.GetCommandLineArgs();
        manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(args[Array.IndexOf(args, "-menuDepthDesktopManifest") + 1]));
        if (Array.IndexOf(UnityEditorInternal.InternalEditorUtility.tags, "UICamera") < 0)
            UnityEditorInternal.InternalEditorUtility.AddTag("UICamera");
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        writer = new StreamWriter(manifest.result);
        writer.WriteLine("Unity " + Application.unityVersion + "; " + SystemInfo.graphicsDeviceName + "; " + SystemInfo.graphicsDeviceType);
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        if (index == manifest.cases.Length)
        {
            writer.Dispose(); EditorApplication.update -= Tick; EditorApplication.Exit(pass ? 0 : 1); return;
        }
        Case item = manifest.cases[index];
        try
        {
            if (routine == null)
            {
                string output = Path.Combine(manifest.evidence, item.name); Directory.CreateDirectory(output);
                Action<int> done = count => assertions = count;
                routine = (IEnumerator)Assembly.LoadFile(item.dll).GetType("MenuDepthDesktopProgram")
                    .GetMethod("Run").Invoke(null, new object[] { output, done });
            }
            if (routine.MoveNext()) return;
            if (!string.IsNullOrEmpty(item.expected)) throw new Exception("Negative control escaped: " + item.name);
            writer.WriteLine("PASS " + item.name + ": " + assertions + " assertions");
        }
        catch (Exception exception)
        {
            while (exception is TargetInvocationException && exception.InnerException != null) exception = exception.InnerException;
            if (!string.IsNullOrEmpty(item.expected) && exception.Message.Contains(item.expected))
                writer.WriteLine("PASS negative control " + item.name + ": " + exception.Message);
            else { pass = false; writer.WriteLine("FAIL " + item.name + ": " + exception); }
        }
        if (routine is IDisposable disposable) disposable.Dispose();
        routine = null; index++; writer.Flush();
        foreach (GameObject go in UnityEngine.Object.FindObjectsOfType<GameObject>()) UnityEngine.Object.DestroyImmediate(go);
    }
}
