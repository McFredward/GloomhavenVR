using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class FrameMrRunner
{
    public static void Run()
    {
        string[] args = Environment.GetCommandLineArgs();
        string result = args[Array.IndexOf(args, "-frameMrResult") + 1];
        try
        {
            Assembly fixture = Assembly.Load("FrameMrPresentation");
            int count = (int)fixture.GetType("FrameMrPresentationProgram").GetMethod("Run").Invoke(null, null);
            File.WriteAllText(result, "PASS: " + count + " production camera/presentation assertions; Unity "
                + Application.unityVersion + "; GPU=" + SystemInfo.graphicsDeviceName + "\n");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
            File.WriteAllText(result, "FAIL: " + error + "\n"); EditorApplication.Exit(1);
        }
    }
}
