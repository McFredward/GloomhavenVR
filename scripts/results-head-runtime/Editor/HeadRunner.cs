using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// One iterator step per actual editor play frame. The production supersampler
// sees Unity's real frame count and its enabled cameras participate normally.
public static class HeadRunner
{
    [Serializable] private class Case { public string name, dll, expected; }
    [Serializable] private class Manifest { public string result, evidence; public Case[] cases; }
    private static Manifest manifest;
    private static StreamWriter output;
    private static Type subject;
    private static int index;
    private static bool passed = true;
    public static void Start()
    {
        string[] args = Environment.GetCommandLineArgs();
        manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(args[Array.IndexOf(args, "-progressionManifest") + 1]));
        output = new StreamWriter(manifest.result);
        output.WriteLine("Unity " + Application.unityVersion + "; " + SystemInfo.graphicsDeviceName);
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        EditorApplication.update += Frame;
        EditorApplication.EnterPlaymode();
    }
    private static void Frame()
    {
        if (!EditorApplication.isPlaying) return;
        if (index >= manifest.cases.Length) {
            output.Dispose(); EditorApplication.update -= Frame; EditorApplication.Exit(passed ? 0 : 1); return;
        }
        Case entry = manifest.cases[index];
        try {
            if (subject == null) {
                string evidence = Path.Combine(manifest.evidence, entry.name);
                Directory.CreateDirectory(evidence);
                subject = Assembly.LoadFile(entry.dll).GetType("ProgressionTests");
                subject.GetMethod("Run").Invoke(null, new object[] { evidence });
                return;
            }
            if ((bool)subject.GetMethod("HeadAdvance").Invoke(null, null)) return;
            if (!String.IsNullOrEmpty(entry.expected)) throw new Exception("negative control escaped: " + entry.name);
            output.WriteLine("PASS " + entry.name + ": " + subject.GetMethod("HeadAssertions").Invoke(null, null) + " actual-coordinator/head-pixel assertions");
        }
        catch (Exception ex) {
            while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
            if (!String.IsNullOrEmpty(entry.expected) && ex.Message.Contains(entry.expected))
                output.WriteLine("PASS negative control " + entry.name + ": " + ex.Message);
            else { passed = false; output.WriteLine("FAIL " + entry.name + ": " + ex); }
        }
        try { subject?.GetMethod("HeadFinish")?.Invoke(null, null); }
        finally {
            foreach (var obj in UnityEngine.Object.FindObjectsOfType<GameObject>()) UnityEngine.Object.DestroyImmediate(obj);
            subject?.GetMethod("Cleanup")?.Invoke(null, null);
            subject = null; index++; output.Flush();
        }
    }
}
