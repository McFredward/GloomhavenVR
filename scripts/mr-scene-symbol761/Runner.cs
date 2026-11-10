using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class SceneSymbolRunner761
{
    [Serializable] private sealed class Case { public string name, dll, expected; }
    [Serializable] private sealed class Manifest { public string result, managed; public Case[] cases; }
    public static void Start()
    {
        string[] args = Environment.GetCommandLineArgs();
        var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(args[Array.IndexOf(args, "-symbolManifest") + 1]));
        AppDomain.CurrentDomain.AssemblyResolve += (sender, item) => {
            string path = Path.Combine(manifest.managed, new AssemblyName(item.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        bool failed = false;
        using (var result = new StreamWriter(manifest.result))
        {
            foreach (var item in manifest.cases)
            {
                try
                {
                    int checks = (int)Assembly.LoadFrom(item.dll).GetType("SceneSymbol761").GetMethod("Run").Invoke(null, null);
                    if (!String.IsNullOrEmpty(item.expected)) throw new Exception("Negative control escaped: " + item.name);
                    result.WriteLine("PASS " + item.name + ": " + checks + " actual native component/admission assertions");
                }
                catch (Exception error)
                {
                    while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
                    if (!String.IsNullOrEmpty(item.expected) && error.Message.Contains(item.expected)) result.WriteLine("PASS negative control " + item.name + ": " + error.Message);
                    else { failed = true; result.WriteLine("FAIL " + item.name + ": " + error); }
                }
                result.Flush();
            }
        }
        EditorApplication.Exit(failed ? 1 : 0);
    }
}
