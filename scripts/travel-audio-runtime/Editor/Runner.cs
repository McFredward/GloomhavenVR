using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class TravelAudioRunner
{
    [Serializable] private class Case { public string name, dll, expected; }
    [Serializable] private class Manifest { public string evidence, result, gameManaged, dependencies; public Case[] cases; }
    private static Manifest manifest;
    private static IEnumerator pending;
    private static StreamWriter output;
    private static Case current;
    private static int index, count;
    private static bool passed = true;

    public static void Start()
    {
        string[] args = Environment.GetCommandLineArgs();
        manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(args[Array.IndexOf(args, "-travelAudioManifest") + 1]));
        AppDomain.CurrentDomain.AssemblyResolve += (sender, entry) => {
            string name = new AssemblyName(entry.Name).Name + ".dll";
            foreach (string folder in new[] { manifest.dependencies, manifest.gameManaged }) {
                string path = Path.Combine(folder, name); if (File.Exists(path)) return Assembly.LoadFrom(path);
            }
            return null;
        };
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        output = new StreamWriter(manifest.result);
        output.WriteLine("Unity " + Application.unityVersion + "; actual GH.Runtime audio; synthetic probe waveforms; physical audio device/HMD not tested");
        EditorApplication.update += Tick; EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        if (pending == null && index == manifest.cases.Length) {
            output.Dispose(); EditorApplication.update -= Tick; EditorApplication.Exit(passed ? 0 : 1); return;
        }
        try {
            if (pending == null) {
                current = manifest.cases[index++]; count = -1;
                string evidence = Path.Combine(manifest.evidence, current.name); Directory.CreateDirectory(evidence);
                pending = (IEnumerator)Assembly.LoadFile(current.dll).GetType("TravelAudioProgram").GetMethod("Run").Invoke(null, new object[] { evidence });
            }
            if (pending.MoveNext()) { if (pending.Current is int number) count = number; return; }
            if (count < 0) throw new Exception("fixture returned no completed assertion count");
            if (!String.IsNullOrEmpty(current.expected)) throw new Exception("negative control escaped: " + current.name);
            output.WriteLine("PASS " + current.name + ": " + count + " actual native/Unity audio assertions");
            End();
        } catch (Exception error) {
            while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
            if (!String.IsNullOrEmpty(current.expected) && error.Message.Contains(current.expected))
                output.WriteLine("PASS negative control " + current.name + ": " + error.Message);
            else { passed = false; output.WriteLine("FAIL " + current.name + ": " + error); }
            End();
        }
    }
    private static void End()
    {
        try { (pending as IDisposable)?.Dispose(); }
        catch (Exception error) { passed = false; output.WriteLine("FAIL fixture cleanup: " + error); }
        pending = null; output.Flush();
    }
}
