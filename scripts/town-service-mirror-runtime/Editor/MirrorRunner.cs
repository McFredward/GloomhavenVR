using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using TMPro;

public static class MirrorRunner
{
    [Serializable] private class Case { public string name, dll, expected; }
    [Serializable] private class Manifest { public string result, evidence, suite; public Case[] cases; }
    private static Manifest manifest;
    private static StreamWriter output;
    private static IEnumerator routine;
    private static int current;
    private static bool started, passed = true;
    public static void Start()
    {
        var args = Environment.GetCommandLineArgs();
        manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(args[Array.IndexOf(args, "-mirrorManifest") + 1]));
        output = new StreamWriter(manifest.result);
        output.WriteLine("Unity " + Application.unityVersion + "; renderer " + SystemInfo.graphicsDeviceName);
        output.Flush();
        AssetDatabase.importPackageCompleted += Imported;
        var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMP_Text).Assembly);
        AssetDatabase.ImportPackage(Path.Combine(package.resolvedPath, "Package Resources/TMP Essential Resources.unitypackage"), false);
    }
    private static void Imported(string packageName)
    {
        AssetDatabase.importPackageCompleted -= Imported;
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        EditorApplication.update += Step;
        EditorApplication.EnterPlaymode();
    }
    private static void Step()
    {
        if (!EditorApplication.isPlaying) return;
        if (current >= manifest.cases.Length) { output.Dispose(); EditorApplication.Exit(passed ? 0 : 1); return; }
        var entry = manifest.cases[current];
        try
        {
            if (!started)
            {
                started = true;
                var type = Assembly.LoadFile(entry.dll).GetType("MirrorProgram");
                routine = (IEnumerator)type.GetMethod("Run").Invoke(null, new object[] { manifest.evidence, entry.name, manifest.suite });
            }
            if (routine.MoveNext()) return;
            if (!String.IsNullOrEmpty(entry.expected)) throw new Exception("negative control escaped: " + entry.name);
            output.WriteLine("PASS " + entry.name);
        }
        catch (Exception error)
        {
            while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
            if (!String.IsNullOrEmpty(entry.expected) && error.Message.Contains(entry.expected))
                output.WriteLine("PASS negative control " + entry.name + ": " + error.Message);
            else { passed = false; output.WriteLine("FAIL " + entry.name + ": " + error); }
        }
        try { (routine as IDisposable)?.Dispose(); } catch (Exception error) { passed = false; output.WriteLine("FAIL cleanup: " + error); }
        routine = null; started = false; current++; output.Flush();
    }
}
