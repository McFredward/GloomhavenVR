using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class LifecycleRunner660
{
    public static void Start()
    {
        string[] args = Environment.GetCommandLineArgs();
        string output = args[Array.IndexOf(args, "-evidence660") + 1];
        string dll = args[Array.IndexOf(args, "-dll660") + 1];
        string expected = args[Array.IndexOf(args, "-expected660") + 1];
        try
        {
            Assembly.LoadFile(dll).GetType("MotionLayout660").GetMethod("Run").Invoke(null, new object[] {output});
            if (!String.IsNullOrEmpty(expected)) throw new Exception("negative control escaped: " + expected);
            File.WriteAllText(Path.Combine(output, "result.txt"), "PASS native layout and parent-lifetime clocks\n");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
            if (!String.IsNullOrEmpty(expected) && error.Message.Contains(expected) && !error.Message.StartsWith("negative control escaped"))
            {
                File.WriteAllText(Path.Combine(output, "result.txt"), "PASS causal control: " + error.Message + "\n");
                EditorApplication.Exit(0); return;
            }
            File.WriteAllText(Path.Combine(output, "result.txt"), "FAIL " + error + "\n");
            EditorApplication.Exit(1);
        }
    }
}
