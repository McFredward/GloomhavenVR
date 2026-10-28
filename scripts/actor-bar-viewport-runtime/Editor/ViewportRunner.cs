using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
public static class ViewportRunner
{
    [Serializable] public sealed class Case { public string name, dll, expected; }
    [Serializable] public sealed class Manifest { public string result, evidence; public Case[] cases; }
    static Manifest manifest;
    static bool ran;
    public static void Start()
    {
        string[] args = Environment.GetCommandLineArgs();
        manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(args[Array.IndexOf(args, "-viewportManifest") + 1]));
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        EditorApplication.update += Run;
        EditorApplication.EnterPlaymode();
    }
    static void Run()
    {
        if (!EditorApplication.isPlaying || ran) return;
        ran = true;
        bool passed = true;
        using (var output = new StreamWriter(manifest.result))
        {
            output.WriteLine("Unity " + Application.unityVersion + "; " + SystemInfo.graphicsDeviceType + "; " + SystemInfo.graphicsDeviceName);
            foreach (Case entry in manifest.cases)
            {
                try
                {
                    string evidence = Path.Combine(manifest.evidence, entry.name);
                    Directory.CreateDirectory(evidence);
                    int checks = (int)Assembly.LoadFile(entry.dll).GetType("ViewportFixture").GetMethod("Run").Invoke(null, new object[] { evidence });
                    if (!string.IsNullOrEmpty(entry.expected)) throw new Exception("Negative control escaped: " + entry.name);
                    output.WriteLine("PASS " + entry.name + ": " + checks + " assertions");
                }
                catch (Exception error)
                {
                    while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
                    if (!string.IsNullOrEmpty(entry.expected) && error.Message.Contains(entry.expected))
                        output.WriteLine("PASS negative control " + entry.name + ": " + error.Message);
                    else { passed = false; output.WriteLine("FAIL " + entry.name + ": " + error); }
                }
                output.Flush();
            }
        }
        EditorApplication.Exit(passed ? 0 : 1);
    }
}
