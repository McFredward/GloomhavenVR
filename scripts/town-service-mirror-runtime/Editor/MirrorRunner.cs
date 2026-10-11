using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using TMPro;

public static class MirrorRunner
{
    [Serializable] private class Case { public string name, dll, expected; }
    [Serializable] private class Manifest { public string result, evidence, suite; public Case[] cases; }
    private static Manifest manifest;
    private static StreamWriter output;
    private static EditorCoroutine routine;
    private static int current;
    private static bool started, passed = true;
    public static void Start()
    {
        var args = Environment.GetCommandLineArgs();
        manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(args[Array.IndexOf(args, "-mirrorManifest") + 1]));
        output = new StreamWriter(manifest.result);
        output.WriteLine("Unity " + Application.unityVersion + "; renderer " + SystemInfo.graphicsDeviceName);
        VerifyCoroutineScheduling();
        output.WriteLine("PASS editor coroutine scheduling: nested routines, custom waits and disposal");
        output.Flush();
        AssetDatabase.importPackageCompleted += Imported;
        var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMP_Text).Assembly);
        AssetDatabase.ImportPackage(Path.Combine(package.resolvedPath, "Package Resources/TMP Essential Resources.unitypackage"), false);
    }
    private static void Imported(string packageName)
    {
        AssetDatabase.importPackageCompleted -= Imported;
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        EditorApplication.update += Step;
        EditorApplication.EnterPlaymode();
    }
    private static void Step()
    {
        if (!EditorApplication.isPlaying) return;
        if (current >= manifest.cases.Length) { output.Dispose(); EditorApplication.Exit(passed ? 0 : 1); return; }
        var entry = manifest.cases[current];
        try
        {
            if (!started)
            {
                started = true;
                var type = Assembly.LoadFile(entry.dll).GetType("MirrorProgram");
                routine = new EditorCoroutine((IEnumerator)type.GetMethod("Run").Invoke(null, new object[] { manifest.evidence, entry.name, manifest.suite }));
            }
            if (routine.Step()) return;
            if (!String.IsNullOrEmpty(entry.expected)) throw new Exception("negative control escaped: " + entry.name);
            output.WriteLine("PASS " + entry.name);
        }
        catch (Exception error)
        {
            while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
            if (!String.IsNullOrEmpty(entry.expected) && error.Message.Contains(entry.expected))
                output.WriteLine("PASS negative control " + entry.name + ": " + error.Message);
            else { passed = false; output.WriteLine("FAIL " + entry.name + ": " + error); }
        }
        try { routine?.Dispose(); } catch (Exception error) { passed = false; output.WriteLine("FAIL cleanup: " + error); }
        routine = null; started = false; current++; output.Flush();
    }

    // EditorApplication.update is not Unity's coroutine scheduler. Ignoring Current
    // silently skipped a nested bank proof and treated a realtime wait as one frame.
    // Honour the yielded native operations; fail rather than certify an unknown wait.
    private sealed class EditorCoroutine : IDisposable
    {
        private readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
        private AsyncOperation pending;
        internal EditorCoroutine(IEnumerator entry) { stack.Push(entry); }
        internal bool Step()
        {
            if (pending != null && !pending.isDone) return true;
            pending = null;
            while (stack.Count != 0)
            {
                IEnumerator current = stack.Peek();
                if (!current.MoveNext()) { stack.Pop(); (current as IDisposable)?.Dispose(); continue; }
                object yielded = current.Current;
                // CustomYieldInstruction implements IEnumerator, including realtime waits.
                if (yielded is IEnumerator nested) { stack.Push(nested); continue; }
                if (yielded is AsyncOperation operation)
                { if (operation.isDone) continue; pending = operation; return true; }
                if (yielded == null || yielded is WaitForEndOfFrame || yielded is WaitForFixedUpdate) return true;
                throw new InvalidOperationException("Unsupported editor coroutine yield: " + yielded.GetType().FullName);
            }
            return false;
        }
        public void Dispose()
        { pending = null; while (stack.Count != 0) (stack.Pop() as IDisposable)?.Dispose(); }
    }

    private static void VerifyCoroutineScheduling()
    {
        int stage = 0, disposed = 0;
        bool ready = false;
        IEnumerator Child()
        {
            try { stage = 1; yield return new WaitUntil(() => ready); stage = 2; yield return null; }
            finally { disposed++; }
        }
        IEnumerator Parent() { yield return Child(); stage = 3; }
        using (var cursor = new EditorCoroutine(Parent()))
        {
            if (!cursor.Step() || stage != 1 || !cursor.Step() || stage != 1)
                throw new Exception("Nested proof or native custom wait was skipped.");
            ready = true;
            if (!cursor.Step() || stage != 2 || cursor.Step() || stage != 3 || disposed != 1)
                throw new Exception("Nested proof did not resume and complete exactly once.");
        }
        ready = false;
        using (var cancelled = new EditorCoroutine(Parent()))
        { if (!cancelled.Step() || stage != 1) throw new Exception("Cancelled nested proof was skipped."); }
        if (disposed != 2) throw new Exception("Cancelled nested proof was not disposed.");
    }
}
