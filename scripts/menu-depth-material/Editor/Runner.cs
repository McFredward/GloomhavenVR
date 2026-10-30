using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class MenuDepthMaterialRunner
{
    [Serializable] private sealed class Case { public string name, dll, expected; }
    [Serializable] private sealed class Manifest { public string evidence, result, mask; public Case[] cases; }
    private static Manifest manifest;
    private static bool ran;
    public static void Start()
    {
        string[] args = Environment.GetCommandLineArgs();
        manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(args[Array.IndexOf(args, "-menuDepthMaterialManifest") + 1]));
        PlayerSettings.colorSpace = ColorSpace.Gamma;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        foreach (string path in Directory.GetFiles("Assets", "*.shader", SearchOption.AllDirectories))
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new Exception("Fixture shader did not compile: " + path);
        }
        EditorApplication.update += Run;
    }
    private static void Run()
    {
        if (ran) return;
        ran = true; bool passed = true;
        using (var writer = new StreamWriter(manifest.result))
        {
            writer.WriteLine("Unity " + Application.unityVersion + "; " + SystemInfo.graphicsDeviceName + "; " + SystemInfo.graphicsDeviceType);
            writer.WriteLine("Reference is source-bound native D3D11 DXBC disassembly compiled for GL, not execution of the original D3D11 program.");
            foreach (Case item in manifest.cases)
            {
                Assembly assembly = null; string output = Path.Combine(manifest.evidence, item.name); Directory.CreateDirectory(output);
                try
                {
                    assembly = Assembly.LoadFile(item.dll);
                    int count = (int)assembly.GetType("MenuDepthMaterialProgram").GetMethod("Run")
                        .Invoke(null, new object[] { output, manifest.mask });
                    if (!string.IsNullOrEmpty(item.expected)) throw new Exception("Negative control escaped: " + item.name);
                    writer.WriteLine("PASS " + item.name + ": " + count + " assertions");
                }
                catch (Exception error)
                {
                    while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
                    if (!string.IsNullOrEmpty(item.expected) && error.Message.Contains(item.expected))
                        writer.WriteLine("PASS negative control " + item.name + ": " + error.Message);
                    else { passed = false; writer.WriteLine("FAIL " + item.name + ": " + error); }
                }
                finally
                {
                    foreach (GameObject go in UnityEngine.Object.FindObjectsOfType<GameObject>()) UnityEngine.Object.DestroyImmediate(go);
                    if (assembly != null)
                    {
                        var messages = (IEnumerable)assembly.GetType("GloomhavenVR.Core.VRLog")
                            .GetField("Messages", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                        using (var log = new StreamWriter(Path.Combine(output, "lifecycle.log")))
                            foreach (object message in messages) log.WriteLine(message);
                        assembly.GetType("GloomhavenVR.WorldUI.MenuDepth.MenuDepthDisplay")
                            .GetMethod("Shutdown", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
                    }
                }
                writer.Flush();
            }
        }
        EditorApplication.Exit(passed ? 0 : 1);
    }
}
