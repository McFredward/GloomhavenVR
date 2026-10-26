using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class MenuDepthRunner
{
    [Serializable] private class Entry { public string name, dll, expected; }
    [Serializable] private class Manifest { public string result, evidence; public Entry[] cases; }
    private static Manifest manifest;
    private static bool ran;
    public static void Start()
    {
        string[] args = Environment.GetCommandLineArgs();
        manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(args[Array.IndexOf(args, "-menuDepthManifest") + 1]));
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        EditorApplication.update += Run;
        EditorApplication.EnterPlaymode();
    }
    private static void Run()
    {
        if (!EditorApplication.isPlaying || ran) return;
        ran = true; bool passed = true;
        using (var writer = new StreamWriter(manifest.result))
        {
            writer.WriteLine("Unity " + Application.unityVersion + "; " + SystemInfo.graphicsDeviceName + "; " + SystemInfo.graphicsDeviceType);
            foreach (Entry entry in manifest.cases)
            {
                Assembly assembly = null;
                string output = Path.Combine(manifest.evidence, entry.name); Directory.CreateDirectory(output);
                try
                {
                    assembly = Assembly.LoadFile(entry.dll);
                    int count = (int)assembly.GetType("MenuDepthProgram").GetMethod("Run").Invoke(null, new object[] { output });
                    if (!string.IsNullOrEmpty(entry.expected)) throw new Exception("Negative control escaped: " + entry.name);
                    writer.WriteLine("PASS " + entry.name + ": " + count + " assertions");
                }
                catch (Exception error)
                {
                    while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
                    if (!string.IsNullOrEmpty(entry.expected) && error.Message.Contains(entry.expected))
                        writer.WriteLine("PASS negative control " + entry.name + ": " + error.Message);
                    else { passed = false; writer.WriteLine("FAIL " + entry.name + ": " + error); }
                }
                finally
                {
                    if (assembly != null)
                    {
                        Type log = assembly.GetType("GloomhavenVR.Core.VRLog");
                        var messages = (IEnumerable)log.GetField("Messages", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                        using (var logWriter = new StreamWriter(Path.Combine(output, "lifecycle.log")))
                            foreach (object message in messages) logWriter.WriteLine(message);
                        assembly.GetType("GloomhavenVR.WorldUI.MenuDepth.MenuDepthDisplay")
                            .GetMethod("Shutdown", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
                    }
                    foreach (GameObject obj in UnityEngine.Object.FindObjectsOfType<GameObject>()) UnityEngine.Object.DestroyImmediate(obj);
                }
                writer.Flush();
            }
        }
        EditorApplication.Exit(passed ? 0 : 1);
    }
}
