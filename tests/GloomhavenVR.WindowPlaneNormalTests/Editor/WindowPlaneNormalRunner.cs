using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class WindowPlaneNormalRunner
{
    [Serializable] private sealed class Case { public string name, dll, expected; }
    [Serializable] private sealed class Manifest { public string result, evidence; public Case[] cases; }
    public static void Run()
    {
        string[] args = Environment.GetCommandLineArgs();
        Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(args[Array.IndexOf(args, "-planeManifest") + 1]));
        bool passed = true;
        using (var writer = new StreamWriter(manifest.result))
        {
            writer.WriteLine("Actual Unity " + Application.unityVersion + "; transform/Vector3 math, no GPU or XR picture");
            foreach (Case item in manifest.cases)
            {
                try
                {
                    string output = Path.Combine(manifest.evidence, item.name); Directory.CreateDirectory(output);
                    Type type = Assembly.LoadFile(item.dll).GetType("WindowPlaneNormalProgram");
                    int assertions = (int)type.GetMethod("Run").Invoke(null, new object[] { output });
                    if (!String.IsNullOrEmpty(item.expected)) throw new Exception("Negative control escaped: " + item.name);
                    writer.WriteLine("PASS " + item.name + ": " + assertions + " actual Unity plane-normal assertions");
                }
                catch (Exception error)
                {
                    while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
                    if (!String.IsNullOrEmpty(item.expected) && error.Message.Contains(item.expected))
                        writer.WriteLine("PASS negative control " + item.name + ": " + error.Message);
                    else { passed = false; writer.WriteLine("FAIL " + item.name + ": " + error); }
                }
                writer.Flush();
            }
        }
        EditorApplication.Exit(passed ? 0 : 1);
    }
}
