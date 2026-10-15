using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class TileHoverRunner
{
    [Serializable] private class Case { public string name, dll, expected; }
    [Serializable] private class Manifest { public string result, native; public Case[] cases; }
    private static Manifest manifest; private static bool ran;
    public static void Start()
    {
        string[] args = Environment.GetCommandLineArgs();
        manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(args[Array.IndexOf(args, "-tileHoverManifest") + 1]));
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        EditorApplication.update += Run; EditorApplication.EnterPlaymode();
    }
    private static void Run()
    {
        if (!EditorApplication.isPlaying || ran) return;
        ran = true; bool passed = true;
        using (var output = new StreamWriter(manifest.result))
        {
            output.WriteLine("Unity " + Application.unityVersion + "; actual shipped hex mesh and BearTrap collider");
            foreach (var entry in manifest.cases)
            {
                var before = new HashSet<int>(); foreach (var go in UnityEngine.Object.FindObjectsOfType<GameObject>(true)) before.Add(go.GetInstanceID());
                try
                {
                    int count = (int)Assembly.LoadFile(entry.dll).GetType("TileHoverProgram").GetMethod("Run").Invoke(null, new object[] { manifest.native });
                    if (!String.IsNullOrEmpty(entry.expected)) throw new Exception("negative control escaped: " + entry.name);
                    output.WriteLine("PASS " + entry.name + ": " + count + " actual native geometry/hover assertions");
                }
                catch (Exception error)
                {
                    while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
                    if (!String.IsNullOrEmpty(entry.expected) && error.Message.Contains(entry.expected)) output.WriteLine("PASS negative control " + entry.name + ": " + error.Message);
                    else { passed = false; output.WriteLine("FAIL " + entry.name + ": " + error); }
                }
                finally
                {
                    foreach (var go in UnityEngine.Object.FindObjectsOfType<GameObject>(true))
                        if (go != null && !before.Contains(go.GetInstanceID())) UnityEngine.Object.DestroyImmediate(go);
                    Physics.SyncTransforms();
                }
                output.Flush();
            }
        }
        EditorApplication.Exit(passed ? 0 : 1);
    }
}
