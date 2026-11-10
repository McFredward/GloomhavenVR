using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class RowRunner
{
    [Serializable] private sealed class Case { public string name, dll, expected, scenario; }
    [Serializable] private sealed class Manifest { public string result, evidence; public Case[] cases; }
    private static Manifest manifest;
    private static StreamWriter output;
    private static IEnumerator running;
    private static int index, frame = -1;
    private static bool failed;

    public static void Start()
    {
        var args = Environment.GetCommandLineArgs();
        manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(args[Array.IndexOf(args, "-rowManifest") + 1]));
        output = new StreamWriter(manifest.result);
        output.WriteLine("Unity " + Application.unityVersion + "; " + SystemInfo.graphicsDeviceName);
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        EditorApplication.update += Step;
        EditorApplication.EnterPlaymode();
    }

    private static void Step()
    {
        if (!EditorApplication.isPlaying || frame == Time.frameCount) return;
        frame = Time.frameCount;
        // Native clocks and LeanTween still advance normally. Pace the editor's otherwise
        // sub-millisecond frames so waveform witnesses remain numerically useful.
        System.Threading.Thread.Sleep(10);
        if (index == manifest.cases.Length)
        {
            output.Dispose(); EditorApplication.update -= Step; Time.timeScale = 1;
            EditorApplication.Exit(failed ? 1 : 0); return;
        }
        var entry = manifest.cases[index];
        try
        {
            var assembly = Assembly.LoadFile(entry.dll);
            var checks = assembly.GetType("InitiativeRowChecks");
            if (running == null)
            {
                string evidence = Path.Combine(manifest.evidence, entry.name);
                Directory.CreateDirectory(evidence);
                running = (IEnumerator)checks.GetMethod("Run").Invoke(null, new object[] { evidence, entry.scenario });
            }
            if (running.MoveNext()) return;
            if (!String.IsNullOrEmpty(entry.expected)) throw new Exception("negative control escaped: " + entry.name);
            output.WriteLine("PASS " + entry.name + ": " + checks.GetField("Assertions").GetValue(null) + " actual row/clock/geometry/pixel assertions");
        }
        catch (Exception error)
        {
            while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
            if (!String.IsNullOrEmpty(entry.expected) && error.Message.Contains(entry.expected))
                output.WriteLine("PASS negative control " + entry.name + ": " + error.Message);
            else { failed = true; output.WriteLine("FAIL " + entry.name + ": " + error); }
        }
        try { (running as IDisposable)?.Dispose(); }
        finally
        {
            running = null; index++; Time.timeScale = 1; LeanTween.reset();
            foreach (var go in UnityEngine.Object.FindObjectsOfType<GameObject>()) UnityEngine.Object.DestroyImmediate(go);
            output.Flush();
        }
    }
}
