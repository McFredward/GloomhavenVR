using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class EnemyRevealPlacementRunner
{
    private static StreamWriter _result;
    private static string _evidence;
    private static bool _passed = true;

    public static void Run()
    {
        string[] args = Environment.GetCommandLineArgs();
        _evidence = args[Array.IndexOf(args, "-enemyRevealEvidence") + 1];
        using (_result = new StreamWriter(Path.Combine(_evidence, "result.txt")))
        {
            _result.WriteLine($"Unity {Application.unityVersion}, {SystemInfo.graphicsDeviceName}");
    // CASES
        }
        EditorApplication.Exit(_passed ? 0 : 1);
    }

    private static void Check(string name, Func<string, int> test, string expected)
    {
        try
        {
            int count = test(expected.Length == 0 ? _evidence : null);
            if (expected.Length == 0)
            {
                _result.WriteLine($"PASS {name}: {count} production placement assertions");
                return;
            }
            _result.WriteLine($"FAIL {name}: negative control escaped");
        }
        catch (Exception error)
        {
            if (expected.Length > 0 && error.Message == expected)
            {
                _result.WriteLine($"PASS {name}: {error.Message}");
                return;
            }
            _result.WriteLine($"FAIL {name}: {error}");
        }
        _passed = false;
    }
}
