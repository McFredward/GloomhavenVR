using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class MenuDepthCharacterRunner
{
    [Serializable] private sealed class Case { public string name, dll, method, expected; }
    [Serializable] private sealed class Manifest { public string result, evidence, harmony; public bool editorMode; public Case[] cases; }
    private static Manifest manifest;
    private static bool ran, updateNoted, finished, pass = true;
    private static StreamWriter writer;
    private static int index, pendingCount;
    private static IEnumerator pending;
    private static Case active;
    public static void Start()
    {
        Debug.Log("MENU DEPTH CHARACTER FIXTURE: Start entered");
        string[] args = Environment.GetCommandLineArgs();
        manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(args[Array.IndexOf(args, "-menuDepthCharacterManifest") + 1]));
        AppDomain.CurrentDomain.AssemblyResolve += (sender, item) =>
        {
            string path = Path.Combine(Path.GetDirectoryName(manifest.harmony), new AssemblyName(item.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFile(path) : null;
        };
        Assembly.LoadFile(manifest.harmony);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        EditorApplication.update += Run;
        if (!manifest.editorMode) EditorApplication.EnterPlaymode();
    }
    private static void Run()
    {
        if (!updateNoted) { updateNoted = true; Debug.Log("MENU DEPTH CHARACTER FIXTURE: update entered; playing=" + EditorApplication.isPlaying); }
        if ((!manifest.editorMode && !EditorApplication.isPlaying) || finished) return;
        if (!ran)
        {
            ran = true; writer = new StreamWriter(manifest.result);
            writer.WriteLine("Unity " + Application.unityVersion + "; " + SystemInfo.graphicsDeviceName + "; " + SystemInfo.graphicsDeviceType);
        }
        while (pending != null || index < manifest.cases.Length)
        {
            if (pending != null)
            {
                try
                {
                    if (pending.MoveNext())
                    {
                        if (pending.Current is int count) pendingCount = count;
                        return; // Real Unity frames/cleanup callbacks continue.
                    }
                    if (pendingCount < 0) throw new Exception("Asynchronous fixture did not return its assertion count.");
                    Success(active, pendingCount);
                }
                catch (Exception error) { Failure(active, error); }
                EndCase();
                continue;
            }
            active = manifest.cases[index++];
            try
            {
                string output = Path.Combine(manifest.evidence, active.name); Directory.CreateDirectory(output);
                string type = active.method.StartsWith("Automatic", StringComparison.Ordinal) ? "MenuDepthCharacterDirectProof"
                    : active.method == "Presentation" || active.method == "NativeLdrEffect" ? "MenuDepthCharacterPresentation725"
                    : active.method == "GlassCabinet" || active.method == "TargetViewport" ? "MenuDepthCharacterWindowProof" : "MenuDepthCharacterProgram";
                object result = Assembly.LoadFile(active.dll).GetType(type).GetMethod(active.method).Invoke(null, new object[] { output });
                if (result is IEnumerator continuation)
                { pending = continuation; pendingCount = -1; continue; }
                Success(active, (int)result);
            }
            catch (Exception error) { Failure(active, error); }
            EndCase();
        }
        finished = true; writer.Dispose(); EditorApplication.Exit(pass ? 0 : 1);
    }
    private static void Success(Case item, int count)
    {
        if (!string.IsNullOrEmpty(item.expected)) throw new Exception("Negative control escaped: " + item.name);
        writer.WriteLine("PASS " + item.name + ": " + count + " assertions");
    }
    private static void Failure(Case item, Exception error)
    {
        while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
        if (!string.IsNullOrEmpty(item.expected) && error.Message.Contains(item.expected))
            writer.WriteLine("PASS negative control " + item.name + ": " + error.Message);
        else { pass = false; writer.WriteLine("FAIL " + item.name + ": " + error); }
    }
    private static void EndCase()
    {
        try { (pending as IDisposable)?.Dispose(); }
        catch (Exception error) { pass = false; writer.WriteLine("FAIL fixture cleanup: " + error); }
        pending = null;
        foreach (GameObject go in UnityEngine.Object.FindObjectsOfType<GameObject>()) UnityEngine.Object.DestroyImmediate(go);
        writer.Flush();
    }
}
