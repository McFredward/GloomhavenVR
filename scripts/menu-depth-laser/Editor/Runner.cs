using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class MenuDepthLaserRunner
{
    [Serializable] private sealed class Case { public string name, dll, expected; }
    [Serializable] private sealed class Manifest { public string result, evidence; public Case[] cases; }
    private static Manifest manifest;
    private static IEnumerator routine;
    private static StreamWriter output;
    private static int index, assertions;
    private static bool passed = true;

    public static void Start()
    {
        string[] args = Environment.GetCommandLineArgs();
        manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(args[Array.IndexOf(args, "-menuDepthLaserManifest") + 1]));
        output = new StreamWriter(manifest.result);
        output.WriteLine("Unity " + Application.unityVersion + "; " + SystemInfo.graphicsDeviceName + "; " + SystemInfo.graphicsDeviceType);
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        if (index == manifest.cases.Length)
        { output.Dispose(); EditorApplication.update -= Tick; EditorApplication.Exit(passed ? 0 : 1); return; }
        Case item = manifest.cases[index];
        try
        {
            if (routine == null)
            {
                string evidence = Path.Combine(manifest.evidence, item.name); Directory.CreateDirectory(evidence);
                Environment.SetEnvironmentVariable("GHVR_LASER_CASE",item.name);
                routine = (IEnumerator)Assembly.LoadFile(item.dll).GetType("MenuDepthLaserProgram")
                    .GetMethod("Run").Invoke(null, new object[] { evidence, new Action<int>(count => assertions = count) });
            }
            if (routine.MoveNext()) return;
            if (!string.IsNullOrEmpty(item.expected)) throw new Exception("Negative control escaped: " + item.name);
            output.WriteLine("PASS " + item.name + ": " + assertions + " assertions");
        }
        catch (Exception error)
        {
            while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
            if (!string.IsNullOrEmpty(item.expected) && error.Message.Contains(item.expected))
                output.WriteLine("PASS negative control " + item.name + ": " + error.Message);
            else { passed = false; output.WriteLine("FAIL " + item.name + ": " + error); }
        }
        if (routine is IDisposable disposable) disposable.Dispose();
        routine = null; index++; output.Flush();
        foreach (GameObject go in UnityEngine.Object.FindObjectsOfType<GameObject>()) UnityEngine.Object.DestroyImmediate(go);
        // Distinct source-bound case assemblies share Unity's native bundle
        // registry. Use the real owner's teardown between those assemblies.
        Assembly.LoadFile(item.dll).GetType("GloomhavenVR.WorldUI.MenuDepth.MenuDepthDisplay")
            .GetMethod("Shutdown",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
    }
}
