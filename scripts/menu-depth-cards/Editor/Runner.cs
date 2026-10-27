using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class MenuDepthCardRunner
{
    [Serializable] private sealed class Case { public string name, dll, expected; }
    [Serializable] private sealed class Manifest { public string result, evidence, gameManaged, harmonyRuntime; public Case[] cases; }
    private static Manifest manifest;
    private static bool ran, passed = true;
    private static StreamWriter writer;
    private static int index, count = -1;
    private static IEnumerator pending;
    private static Case active;
    private static Assembly assembly;
    public static void Start()
    {
        string[] args = Environment.GetCommandLineArgs();
        manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(args[Array.IndexOf(args, "-menuDepthCardManifest") + 1]));
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        AssetDatabase.importPackageCompleted += Imported;
        var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.textmeshpro/Scripts/Runtime/TMP_Text.cs");
        AssetDatabase.ImportPackage(Path.Combine(package.resolvedPath, "Package Resources/TMP Essential Resources.unitypackage"), false);
    }
    private static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        string name = new AssemblyName(args.Name).Name + ".dll";
        foreach (string root in new[] { manifest.gameManaged, manifest.harmonyRuntime })
        {
            string path = Path.Combine(root, name); if (File.Exists(path)) return Assembly.LoadFrom(path);
        }
        return null;
    }
    private static void Imported(string package)
    {
        AssetDatabase.importPackageCompleted -= Imported;
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        EditorApplication.update += Run; EditorApplication.EnterPlaymode();
    }
    private static void Run()
    {
        if (!EditorApplication.isPlaying) return;
        if (!ran) { ran = true; writer = new StreamWriter(manifest.result); writer.WriteLine("Unity " + Application.unityVersion + "; " + SystemInfo.graphicsDeviceName + "; " + SystemInfo.graphicsDeviceType); }
        while (pending != null || index < manifest.cases.Length)
        {
            try
            {
                if (pending == null)
                {
                    active = manifest.cases[index++]; count = -1;
                    string output = Path.Combine(manifest.evidence, active.name); Directory.CreateDirectory(output);
                    assembly = Assembly.LoadFile(active.dll);
                    pending = (IEnumerator)assembly.GetType("MenuDepthCardProgram").GetMethod("Run").Invoke(null, new object[] { output });
                }
                if (pending.MoveNext()) { if (pending.Current is int checks) count = checks; return; }
                if (count < 0) throw new Exception("Native card coroutine returned no assertion count.");
                if (!string.IsNullOrEmpty(active.expected)) throw new Exception("Negative control escaped: " + active.name);
                writer.WriteLine("PASS " + active.name + ": " + count + " native card/GPU assertions");
            }
            catch (Exception error)
            {
                while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
                if (!string.IsNullOrEmpty(active.expected) && error.Message.Contains(active.expected)) writer.WriteLine("PASS negative control " + active.name + ": " + error.Message);
                else { passed = false; writer.WriteLine("FAIL " + active.name + ": " + error); }
            }
            if (pending is IDisposable disposable) disposable.Dispose(); pending = null;
            if (assembly != null)
            {
                var logs = (IEnumerable)assembly.GetType("GloomhavenVR.Core.VRLog").GetField("Messages", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                using (var log = new StreamWriter(Path.Combine(manifest.evidence, active.name, "lifecycle.log"))) foreach (object value in logs) log.WriteLine(value);
            }
            foreach (GameObject value in UnityEngine.Object.FindObjectsOfType<GameObject>()) UnityEngine.Object.DestroyImmediate(value);
            writer.Flush();
        }
        writer.Dispose(); EditorApplication.update -= Run; EditorApplication.Exit(passed ? 0 : 1);
    }
}
