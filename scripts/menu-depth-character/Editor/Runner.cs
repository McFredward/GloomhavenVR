using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class MenuDepthCharacterRunner
{
    [Serializable] private sealed class Case { public string name, dll, method, expected; }
    [Serializable] private sealed class Manifest { public string result, evidence, harmony; public Case[] cases; }
    private static Manifest manifest;
    private static bool ran;
    public static void Start()
    {
        string[] args = Environment.GetCommandLineArgs();
        manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(args[Array.IndexOf(args, "-menuDepthCharacterManifest") + 1]));
        AppDomain.CurrentDomain.AssemblyResolve += (sender, item) =>
        {
            string path = Path.Combine(Path.GetDirectoryName(manifest.harmony), new AssemblyName(item.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFile(path) : null;
        };
        Assembly.LoadFile(manifest.harmony);
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        EditorApplication.update += Run;
        EditorApplication.EnterPlaymode();
    }
    private static void Run()
    {
        if (!EditorApplication.isPlaying || ran) return;
        ran = true;
        bool pass = true;
        using (var writer = new StreamWriter(manifest.result))
        {
            writer.WriteLine("Unity " + Application.unityVersion + "; " + SystemInfo.graphicsDeviceName + "; " + SystemInfo.graphicsDeviceType);
            foreach (Case item in manifest.cases)
            {
                try
                {
                    string output = Path.Combine(manifest.evidence, item.name); Directory.CreateDirectory(output);
                    int count = (int)Assembly.LoadFile(item.dll).GetType("MenuDepthCharacterProgram")
                        .GetMethod(item.method).Invoke(null, new object[] { output });
                    if (!string.IsNullOrEmpty(item.expected)) throw new Exception("Negative control escaped: " + item.name);
                    writer.WriteLine("PASS " + item.name + ": " + count + " assertions");
                }
                catch (Exception exception)
                {
                    while (exception is TargetInvocationException && exception.InnerException != null) exception = exception.InnerException;
                    if (!string.IsNullOrEmpty(item.expected) && exception.Message.Contains(item.expected))
                        writer.WriteLine("PASS negative control " + item.name + ": " + exception.Message);
                    else { pass = false; writer.WriteLine("FAIL " + item.name + ": " + exception); }
                }
                finally
                {
                    foreach (GameObject go in UnityEngine.Object.FindObjectsOfType<GameObject>()) UnityEngine.Object.DestroyImmediate(go);
                }
                writer.Flush();
            }
        }
        EditorApplication.Exit(pass ? 0 : 1);
    }
}
