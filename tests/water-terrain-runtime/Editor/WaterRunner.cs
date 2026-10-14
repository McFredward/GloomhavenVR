using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class WaterRunner
{
    [Serializable] private class Case { public string name, dll, expected; }
    [Serializable] private class Manifest { public string result; public Case[] cases; }
    private static Manifest manifest;
    private static bool ran;
    public static void Start()
    {
        string[] args = Environment.GetCommandLineArgs();
        manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(args[Array.IndexOf(args, "-waterManifest") + 1]));
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        EditorApplication.update += Run;
        EditorApplication.EnterPlaymode();
    }
    private static void Run()
    {
        if (!EditorApplication.isPlaying || ran) return;
        ran = true; bool passed = true;
        using (var output = new StreamWriter(manifest.result))
        {
            output.WriteLine("Unity " + Application.unityVersion + "; GPU " + SystemInfo.graphicsDeviceName);
            foreach (Case entry in manifest.cases)
            {
                Assembly assembly = null;
                var previous = new HashSet<int>();
                foreach (GameObject go in UnityEngine.Object.FindObjectsOfType<GameObject>(true)) previous.Add(go.GetInstanceID());
                try
                {
                    assembly = Assembly.LoadFile(entry.dll);
                    string program = entry.name.StartsWith("queue-") ? "WaterQueuePixels" : "WaterTerrainProgram";
                    int count = (int)assembly.GetType(program).GetMethod("Run").Invoke(null, null);
                    if (!String.IsNullOrEmpty(entry.expected)) throw new Exception("negative control escaped");
                    output.WriteLine("PASS " + entry.name + ": " + count + " assertions");
                }
                catch (Exception error)
                {
                    while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
                    if (!String.IsNullOrEmpty(entry.expected) && error.Message.Contains(entry.expected))
                        output.WriteLine("PASS negative " + entry.name + ": " + error.Message);
                    else { passed = false; output.WriteLine("FAIL " + entry.name + ": " + error); }
                }
                finally
                {
                    if (assembly != null)
                    {
                        assembly.GetType("GloomhavenVR.Core.WaterTerrainVR").GetMethod("Uninstall", BindingFlags.Static | BindingFlags.Public).Invoke(null, null);
                        assembly.GetType("GloomhavenVR.Core.NativePublicationIntegration").GetMethod("Shutdown", BindingFlags.Static | BindingFlags.Public).Invoke(null, null);
                    }
                    foreach (GameObject go in UnityEngine.Object.FindObjectsOfType<GameObject>(true))
                        if (go != null && !previous.Contains(go.GetInstanceID())) UnityEngine.Object.DestroyImmediate(go);
                    Physics.SyncTransforms();
                }
                output.Flush();
            }
        }
        EditorApplication.Exit(passed ? 0 : 1);
    }
}
