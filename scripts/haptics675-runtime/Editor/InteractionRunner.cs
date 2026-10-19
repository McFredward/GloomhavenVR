using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class InteractionRunner
{
    [Serializable] private class Case { public string name, dll, expected; }
    [Serializable] private class Manifest { public string result; public Case[] cases; }
    private static Manifest manifest;
    private static bool ran;
    public static void Start()
    {
        string[] args = Environment.GetCommandLineArgs();
        manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(args[Array.IndexOf(args, "-interactionManifest") + 1]));
        string managed = args[Array.IndexOf(args, "-nativeGameManaged") + 1];
        // Load publisher assemblies only when the case asks for them, after entering play mode.
        // Importing every player DLL would run unrelated full-game bootstrap callbacks here.
        AppDomain.CurrentDomain.AssemblyResolve += (sender, entry) => {
            string file = Path.Combine(managed, new AssemblyName(entry.Name).Name + ".dll");
            return File.Exists(file) ? Assembly.LoadFrom(file) : null;
        };
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        EditorApplication.update += RunWhenPlaying;
        EditorApplication.EnterPlaymode();
    }
    private static void RunWhenPlaying()
    {
        if (!EditorApplication.isPlaying || ran) return;
        ran = true; bool passed = true;
        using (var output = new StreamWriter(manifest.result))
        {
            output.WriteLine("Unity " + Application.unityVersion);
            foreach (var entry in manifest.cases)
            {
                try
                {
                    var assembly = Assembly.LoadFile(entry.dll);
                    int count = (int)assembly.GetType("InteractionProgram").GetMethod("Run").Invoke(null, null);
                    if (!String.IsNullOrEmpty(entry.expected)) throw new Exception("negative control escaped: " + entry.name);
                    output.WriteLine("PASS " + entry.name + ": " + count + " runtime assertions");
                }
                catch (Exception error)
                {
                    while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
                    if (!String.IsNullOrEmpty(entry.expected) && error.Message.Contains(entry.expected))
                        output.WriteLine("PASS negative control " + entry.name + ": " + error.Message);
                    else { passed = false; output.WriteLine("FAIL " + entry.name + ": " + error); }
                }
                output.Flush();
            }
        }
        EditorApplication.Exit(passed ? 0 : 1);
    }
}
