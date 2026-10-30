using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class MenuDepthDesktopRunner
{
    [Serializable] private sealed class Case { public string name, dll, expected, method; }
    [Serializable] private sealed class Manifest { public string result, evidence, depthBundle; public Case[] cases; }
    private static Manifest manifest;
    private static StreamWriter writer;
    private static StreamWriter timings;
    private static readonly System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();
    private static IEnumerator routine;
    private static int index;
    private static int assertions;
    private static bool pass = true;
    private static AssetBundle depthBundle;
    public static void Start()
    {
        string[] args = Environment.GetCommandLineArgs();
        manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(args[Array.IndexOf(args, "-menuDepthDesktopManifest") + 1]));
        if (Array.IndexOf(UnityEditorInternal.InternalEditorUtility.tags, "UICamera") < 0)
            UnityEditorInternal.InternalEditorUtility.AddTag("UICamera");
        depthBundle = AssetBundle.LoadFromFile(manifest.depthBundle);
        if(depthBundle==null)throw new Exception("Actual production depth bundle missing");
        var optionalNative=depthBundle.LoadAsset<Shader>("assets/menudepth/menudepthnativecard.shader");
        if(optionalNative==null||optionalNative.name!="GloomhavenVR/MenuDepthNativeCard")throw new Exception("Audited optional native card shader cannot load");
        Debug.Log("Exact native card shader loaded; supported="+optionalNative.isSupported+" on "+SystemInfo.graphicsDeviceType+"; original programs are D3D11-only.");
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        writer = new StreamWriter(manifest.result);
        timings = new StreamWriter(Path.Combine(manifest.evidence, "case-timings.csv"));
        timings.WriteLine("case,elapsedSeconds"); timings.Flush();
        writer.WriteLine("Unity " + Application.unityVersion + "; " + SystemInfo.graphicsDeviceName + "; " + SystemInfo.graphicsDeviceType);
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        if (index == manifest.cases.Length)
        {
            timings.Dispose(); writer.Dispose(); EditorApplication.update -= Tick; EditorApplication.Exit(pass ? 0 : 1); return;
        }
        Case item = manifest.cases[index];
        try
        {
            if (routine == null)
            {
                clock.Restart();
                string output = Path.Combine(manifest.evidence, item.name); Directory.CreateDirectory(output);
                Action<int> done = count => assertions = count;
                Assembly assembly=Assembly.LoadFile(item.dll);
                Type display=assembly.GetType("GloomhavenVR.WorldUI.MenuDepth.MenuDepthDisplay");
                foreach(string part in new[]{"display","graphic","character","headresolve"})
                    display.GetField(part=="graphic"?"_depthShader":part=="headresolve"?"_headResolveShader":"_"+part+"Shader",BindingFlags.Static|BindingFlags.NonPublic)
                        .SetValue(null,depthBundle.LoadAsset<Shader>("assets/menudepth/menudepth"+part+".shader"));
                if(item.method=="HeadNativeCardEdge732"||item.method=="HeadFigureCard732")
                    display.GetField("_nativeCardShader",BindingFlags.Static|BindingFlags.NonPublic)
                        .SetValue(null,Shader.Find("GloomhavenVR/FixtureNativeCardDepth"));
                routine = (IEnumerator)assembly.GetType("MenuDepthDesktopProgram")
                    .GetMethod(item.method).Invoke(null, new object[] { output, done });
            }
            if (routine.MoveNext()) return;
            if (!string.IsNullOrEmpty(item.expected)) throw new Exception("Negative control escaped: " + item.name);
            writer.WriteLine("PASS " + item.name + ": " + assertions + " assertions");
        }
        catch (Exception exception)
        {
            while (exception is TargetInvocationException && exception.InnerException != null) exception = exception.InnerException;
            if (!string.IsNullOrEmpty(item.expected) && exception.Message.Contains(item.expected))
                writer.WriteLine("PASS negative control " + item.name + ": " + exception.Message);
            else { pass = false; writer.WriteLine("FAIL " + item.name + ": " + exception); }
        }
        if (routine is IDisposable disposable) disposable.Dispose();
        timings.WriteLine(item.name + "," + clock.Elapsed.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        timings.Flush();
        routine = null; index++; writer.Flush();
        foreach (GameObject go in UnityEngine.Object.FindObjectsOfType<GameObject>()) UnityEngine.Object.DestroyImmediate(go);
    }
}
