using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class MenuDepthPerformanceRunner
{
    [Serializable] private sealed class Case { public string name, dll, expected; public bool baseline, firstDraw, restoreAfterRender, prepareBeforeRender, automatic; }
    [Serializable] private sealed class Manifest { public string result, evidence, gameManaged, harmonyRuntime; public bool automatic; public Case[] cases; }
    private static Manifest manifest;
    private static int index;
    private static Type proof;
    private static StreamWriter results;
    private static bool started, pass = true;
    public static void Start()
    {
        string[] args = Environment.GetCommandLineArgs();
        manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(args[Array.IndexOf(args, "-menuDepthPerfManifest") + 1]));
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        results = new StreamWriter(manifest.result);
        results.WriteLine("Unity " + Application.unityVersion + "; " + SystemInfo.graphicsDeviceName + "; actual Playmode frames"); results.Flush();
        File.WriteAllText(Path.Combine(manifest.evidence, "startup.txt"), "Start entered; actual Playmode updates scheduled.\n");
        EditorApplication.update += Update;
        if (manifest.automatic)
        {
            Type gameView = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView", true);
            EditorWindow.GetWindow(gameView).Show(); EditorWindow.GetWindow(gameView).Focus();
        }
        EditorApplication.EnterPlaymode();
    }
    private static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        string filename = new AssemblyName(args.Name).Name + ".dll";
        foreach (string folder in new[] { manifest.gameManaged, manifest.harmonyRuntime })
        {
            string path = Path.Combine(folder, filename);
            if (File.Exists(path)) return Assembly.LoadFrom(path);
        }
        return null;
    }
    private static void Update()
    {
        if (!EditorApplication.isPlaying) return;
        if (index >= manifest.cases.Length)
        { results.Dispose(); EditorApplication.update -= Update; EditorApplication.Exit(pass ? 0 : 1); return; }
        Case item = manifest.cases[index];
        try
        {
            if (!started)
            {
                started = true;
                string output = Path.Combine(manifest.evidence, item.name); Directory.CreateDirectory(output);
                proof = Assembly.LoadFile(item.dll).GetType("MenuDepthPerformanceProof", true);
                proof.GetMethod("Begin").Invoke(null, new object[] { output, item.baseline, item.firstDraw, item.restoreAfterRender, item.prepareBeforeRender, item.automatic });
                return;
            }
            if (!(bool)proof.GetMethod("Step").Invoke(null, null)) return;
            int assertions = (int)proof.GetMethod("Finish").Invoke(null, null);
            if (!string.IsNullOrEmpty(item.expected)) throw new Exception("Negative control escaped: " + item.name);
            results.WriteLine("PASS " + item.name + ": " + assertions + " assertions");
        }
        catch (Exception exception)
        {
            while (exception is TargetInvocationException && exception.InnerException != null) exception = exception.InnerException;
            if (!string.IsNullOrEmpty(item.expected) && exception.Message.Contains(item.expected))
                results.WriteLine("PASS negative control " + item.name + ": " + exception.Message);
            else { pass = false; results.WriteLine("FAIL " + item.name + ": " + exception); }
        }
        finally { results.Flush(); }
        try { proof?.GetMethod("Dispose").Invoke(null, null); }
        catch (Exception error) { pass = false; results.WriteLine("FAIL cleanup " + item.name + ": " + error); }
        started = false; proof = null; index++; results.Flush();
    }
}
