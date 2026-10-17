using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class StereoVideoRunner
{
    [Serializable] private class Entry { public string name, dll, expected, method; }
    [Serializable] private class Manifest { public string result, evidence, dependencies; public Entry[] cases; }
    private static Manifest manifest;
    private static bool ran;
    public static void Start()
    {
        string[] args = Environment.GetCommandLineArgs();
        manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(args[Array.IndexOf(args, "-stereoManifest") + 1]));
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        if (Array.IndexOf(UnityEditorInternal.InternalEditorUtility.tags, "UICamera") < 0)
            UnityEditorInternal.InternalEditorUtility.AddTag("UICamera");
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        EditorApplication.update += Run;
        EditorApplication.EnterPlaymode();
    }
    private static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        string name = new AssemblyName(args.Name).Name + ".dll";
        string path = Path.Combine(manifest.dependencies ?? string.Empty, name);
        return File.Exists(path) ? Assembly.LoadFile(path) : null;
    }
    private static void Run()
    {
        if (!EditorApplication.isPlaying || ran) return;
        ran = true;
        bool pass = true;
        using (var writer = new StreamWriter(manifest.result))
        {
            writer.WriteLine("Unity " + Application.unityVersion + "; " + SystemInfo.graphicsDeviceName + "; " + SystemInfo.graphicsDeviceType);
            foreach (var entry in manifest.cases)
            {
                try
                {
                    string output = Path.Combine(manifest.evidence, entry.name);
                    Directory.CreateDirectory(output);
                    int count = (int)Assembly.LoadFile(entry.dll).GetType("StereoVideoRenderProgram")
                        .GetMethod(string.IsNullOrEmpty(entry.method) ? "Run" : entry.method).Invoke(null, new object[] { output });
                    if (!string.IsNullOrEmpty(entry.expected)) throw new Exception("Negative control escaped: " + entry.name);
                    writer.WriteLine("PASS " + entry.name + ": " + count + " assertions");
                }
                catch (Exception ex)
                {
                    while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
                    if (!string.IsNullOrEmpty(entry.expected) && ex.Message.Contains(entry.expected))
                        writer.WriteLine("PASS negative control " + entry.name + ": " + ex.Message);
                    else { pass = false; writer.WriteLine("FAIL " + entry.name + ": " + ex); }
                }
                finally
                {
                    foreach (var obj in UnityEngine.Object.FindObjectsOfType<GameObject>()) UnityEngine.Object.DestroyImmediate(obj);
                }
                writer.Flush();
            }
        }
        EditorApplication.Exit(pass ? 0 : 1);
    }
}
