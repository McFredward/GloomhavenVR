using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class ClockRunner
{
    [Serializable] private sealed class Case { public string name, dll, expected; }
    [Serializable] private sealed class Manifest { public string result; public Case[] cases; }
    private static Manifest manifest;
    private static int waitFrame;
    private static bool started, ran;
    private static Type transientProgram;
    public static void Start()
    {
        string[] args = Environment.GetCommandLineArgs();
        manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(args[Array.IndexOf(args, "-clockManifest") + 1]));
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        EditorApplication.update += RunWhenPaused;
        EditorApplication.EnterPlaymode();
    }
    private static void RunWhenPaused()
    {
        if (!EditorApplication.isPlaying || ran) return;
        if (!started) { started = true; waitFrame = Time.frameCount + 2; Time.timeScale = 0f; return; }
        if (Time.frameCount < waitFrame || Time.deltaTime != 0f || Time.unscaledDeltaTime <= 0f) return;
        // Batchmode can run sub-millisecond frames, below Quaternion.Angle's
        // useful precision for one smooth-turn step. Pace a real frame; do not
        // replace native Time or inject the movement under test.
        if (Time.unscaledDeltaTime < .005f) { System.Threading.Thread.Sleep(12); return; }
        ran = true; bool passed = true;
        using (var output = new StreamWriter(manifest.result))
        {
            output.WriteLine("Unity " + Application.unityVersion + " actual clock " + Time.deltaTime + "/" + Time.unscaledDeltaTime);
            foreach (var entry in manifest.cases)
            {
                try
                {
                    int count = (int)Assembly.LoadFile(entry.dll).GetType("ClockProgram").GetMethod("Run").Invoke(null, null);
                    if (!String.IsNullOrEmpty(entry.expected)) throw new Exception("negative control escaped: " + entry.name);
                    output.WriteLine("PASS " + entry.name + ": " + count + " native runtime assertions");
                    if (entry.name == "production") transientProgram = Assembly.LoadFile(entry.dll).GetType("ClockProgram");
                }
                catch (Exception error)
                {
                    while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
                    if (!String.IsNullOrEmpty(entry.expected) && error.Message.Contains(entry.expected))
                        output.WriteLine("PASS causal old-source control " + entry.name + ": " + error.Message);
                    else { passed = false; output.WriteLine("FAIL " + entry.name + ": " + error); }
                }
                finally { foreach (var obj in UnityEngine.Object.FindObjectsOfType<GameObject>()) UnityEngine.Object.DestroyImmediate(obj); }
                output.Flush();
            }
        }
        if (passed && transientProgram != null)
        {
            // Capture a real paused request before resuming the native Unity clock.
            transientProgram.GetMethod("BeginTransientClock").Invoke(null, null);
            Time.timeScale = 1f; waitFrame = Time.frameCount + 2;
            EditorApplication.update -= RunWhenPaused;
            EditorApplication.update += RunWhenResumed;
            return;
        }
        Time.timeScale = 1f;
        EditorApplication.Exit(passed ? 0 : 1);
    }
    private static void RunWhenResumed()
    {
        if (Time.frameCount < waitFrame || Time.deltaTime <= 0f) return;
        EditorApplication.update -= RunWhenResumed;
        bool passed = true;
        using (var output = new StreamWriter(manifest.result, true))
        {
            try
            {
                int count = (int)transientProgram.GetMethod("CheckTransientClock").Invoke(null, null);
                output.WriteLine("PASS production transient clock: " + count + " native runtime assertions; resumed delta=" + Time.deltaTime);
            }
            catch (Exception error)
            {
                while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
                passed = false; output.WriteLine("FAIL production transient clock: " + error);
            }
        }
        EditorApplication.Exit(passed ? 0 : 1);
    }
}
