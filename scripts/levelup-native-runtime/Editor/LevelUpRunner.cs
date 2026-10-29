using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class LevelUpRunner
{
    [Serializable] private sealed class Case { public string name, dll, expected; }
    [Serializable] private sealed class Manifest { public string result, evidence, harmony, managed, inputs; public Case[] cases; }
    private static Manifest manifest;
    private static StreamWriter writer, timings;
    private static readonly System.Diagnostics.Stopwatch runClock = new System.Diagnostics.Stopwatch();
    private static readonly System.Diagnostics.Stopwatch caseClock = new System.Diagnostics.Stopwatch();
    private static IEnumerator pending;
    private static Type program;
    private static Case active;
    private static int index, count;
    private static bool passed = true;
    public static void Start()
    {
        string[] args = Environment.GetCommandLineArgs();
        manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(args[Array.IndexOf(args, "-levelUpManifest") + 1]));
        AppDomain.CurrentDomain.AssemblyResolve += (sender, item) =>
        {
            string name = new AssemblyName(item.Name).Name + ".dll";
            string path = Path.Combine(Path.GetDirectoryName(manifest.harmony), name);
            if (!File.Exists(path)) path = Path.Combine(manifest.managed, name);
            return File.Exists(path) ? Assembly.LoadFile(path) : null;
        };
        Assembly.LoadFile(manifest.harmony);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        writer = new StreamWriter(manifest.result);
        timings = new StreamWriter(Path.Combine(manifest.evidence, "case-timings.csv"));
        timings.WriteLine("event,case,elapsedMs,runElapsedMs"); timings.Flush(); runClock.Restart();
        writer.WriteLine("Unity " + Application.unityVersion + "; " + SystemInfo.graphicsDeviceName + "; " + SystemInfo.graphicsDeviceType);
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        while (pending != null || index < manifest.cases.Length)
        {
            try
            {
                if (pending == null)
                {
                    active = manifest.cases[index++];
                    caseClock.Restart();
                    timings.WriteLine("BEGIN," + active.name + ",0," + runClock.ElapsedMilliseconds); timings.Flush();
                    string output = Path.Combine(manifest.evidence, active.name); Directory.CreateDirectory(output);
                    program = Assembly.LoadFile(active.dll).GetType("LevelUpNativeProgram");
                    program.GetMethod("Inputs").Invoke(null, new object[] { manifest.inputs });
                    pending = (IEnumerator)program.GetMethod("Run").Invoke(null, new object[] { output }); count = -1;
                }
                if (pending.MoveNext())
                {
                    if (pending.Current is int result) count = result;
                    return;
                }
                if (!String.IsNullOrEmpty(active.expected)) throw new Exception("Negative control escaped: " + active.name);
                if (count < 0) throw new Exception("No completed assertion count");
                writer.WriteLine("PASS " + active.name + ": " + count + " actual native widget/Unity scene/pixel assertions");
            }
            catch (Exception error)
            {
                while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
                if (!String.IsNullOrEmpty(active.expected) && error.Message.Contains(active.expected))
                    writer.WriteLine("PASS negative control " + active.name + ": " + error.Message);
                else { passed = false; writer.WriteLine("FAIL " + active.name + ": " + error); }
            }
            try { (pending as IDisposable)?.Dispose(); program?.GetMethod("Cleanup").Invoke(null, null); }
            catch (Exception error) { passed = false; writer.WriteLine("FAIL cleanup: " + error); }
            timings.WriteLine("END," + active.name + "," + caseClock.ElapsedMilliseconds + "," + runClock.ElapsedMilliseconds); timings.Flush();
            pending = null; program = null; writer.Flush();
            return; // Destruction, deferred Unity callbacks and async scene retirement get a real frame.
        }
        timings.Dispose(); writer.Dispose(); EditorApplication.update -= Tick; EditorApplication.Exit(passed ? 0 : 1);
    }
}
