using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
public static class BindingRunner
{
    [Serializable] private class Case { public string name, dll, expected; }
    [Serializable] private class Manifest { public string result; public Case[] cases; }
    public static void Run()
    {
        string[] args = Environment.GetCommandLineArgs();
        var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(args[Array.IndexOf(args,"-bindingManifest")+1]));
        bool failed = false;
        using(var writer = new StreamWriter(manifest.result))
        {
            writer.WriteLine("Unity " + Application.unityVersion);
            foreach(var entry in manifest.cases)
            {
                try
                {
                    Assembly assembly = Array.Find(AppDomain.CurrentDomain.GetAssemblies(), a => a.GetName().Name == Path.GetFileNameWithoutExtension(entry.dll));
                    string result = (string)assembly.GetType("InitiativeBindingChecks").GetMethod("Run").Invoke(null, new object[] { Path.GetDirectoryName(manifest.result) });
                    if (!String.IsNullOrEmpty(entry.expected)) throw new Exception("negative control escaped: " + entry.name);
                    writer.WriteLine("PASS " + entry.name + ": " + result);
                }
                catch (Exception error)
                {
                    while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
                    if (!String.IsNullOrEmpty(entry.expected) && error.Message.Contains(entry.expected)) writer.WriteLine("PASS negative control " + entry.name + ": " + error.Message);
                    else { failed = true; writer.WriteLine("FAIL " + entry.name + ": " + error); }
                }
                writer.Flush();
            }
        }
        EditorApplication.Exit(failed ? 1 : 0);
    }
}
