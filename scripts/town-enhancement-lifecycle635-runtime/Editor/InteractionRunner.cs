using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class InteractionRunner
{
    [Serializable] private class Case { public string name, dll, expected; }
    [Serializable] private class Manifest { public string result, managed, dependencies; public Case[] cases; }
    private static Manifest manifest;
    private static bool ran;
    public static void Start()
    {
        string[] args = Environment.GetCommandLineArgs();
        manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(args[Array.IndexOf(args, "-interactionManifest") + 1]));
        var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var list = tags.FindProperty("tags"); list.InsertArrayElementAtIndex(list.arraySize);
        list.GetArrayElementAtIndex(list.arraySize - 1).stringValue = "UICamera"; tags.ApplyModifiedProperties();
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) => {
            string name = new AssemblyName(e.Name).Name + ".dll";
            string dependency = Path.Combine(manifest.dependencies, name);
            if (File.Exists(dependency)) return Assembly.LoadFrom(dependency);
            string native = Path.Combine(manifest.managed, name);
            // The editor already owns Unity's native engine assemblies.
            return File.Exists(native) && !name.StartsWith("UnityEngine") && !name.StartsWith("System.")
                ? Assembly.LoadFrom(native) : null;
        };
        foreach (string assembly in new[] { "GH.Runtime.dll", "GH.Runtime.FirstPass.dll", "ThirdParty.dll" })
            Assembly.LoadFrom(Path.Combine(manifest.managed, assembly));
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
                var nativeErrors = new List<string>();
                Application.LogCallback trace = (message, stack, type) => {
                    if (type == LogType.Exception || type == LogType.Error) nativeErrors.Add(message);
                };
                Application.logMessageReceived += trace;
                var existing = new HashSet<int>();
                foreach (var obj in UnityEngine.Object.FindObjectsOfType<GameObject>(true)) existing.Add(obj.GetInstanceID());
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
                finally
                {
                    // A negative control intentionally throws before fixture cleanup. Its
                    // colliders must not poison the next isolated assembly's physics scene.
                    foreach (var obj in UnityEngine.Object.FindObjectsOfType<GameObject>(true))
                        if (obj != null && !existing.Contains(obj.GetInstanceID())) UnityEngine.Object.DestroyImmediate(obj);
                    Physics.SyncTransforms();
                    Application.logMessageReceived -= trace;
                    foreach (string nativeError in nativeErrors)
                    { passed = false; output.WriteLine("FAIL " + entry.name + " unexpected native engine error: " + nativeError); }
                }
                output.Flush();
            }
        }
        EditorApplication.Exit(passed ? 0 : 1);
    }
}
