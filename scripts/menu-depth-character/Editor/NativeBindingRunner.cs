using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Separate smoke of the retained actual plugin against the original GH.Runtime.
// No game prefab, manager Start, Display, native animation or gameplay is invoked.
public static class MenuDepthCharacterNativeBindingRunner
{
    [Serializable] private sealed class Manifest { public string plugin, gameManaged, packageRuntime, harmonyRuntime, result; }
    private static Manifest manifest;
    private static bool ran;
    public static void Start()
    {
        string[] args = Environment.GetCommandLineArgs();
        manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(args[Array.IndexOf(args, "-nativeBindingManifest") + 1]));
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        EditorApplication.update += Run;
    }
    private static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        string name = new AssemblyName(args.Name).Name + ".dll";
        foreach (string folder in new[] { manifest.gameManaged, manifest.packageRuntime, manifest.harmonyRuntime, Path.GetDirectoryName(manifest.plugin) })
        {
            string path = Path.Combine(folder, name);
            if (File.Exists(path)) return Assembly.LoadFrom(path);
        }
        return null;
    }
    private static void Run()
    {
        if (ran) return;
        ran = true;
        GameObject go = null;
        try
        {
            Assembly native = Assembly.LoadFrom(Path.Combine(manifest.gameManaged, "GH.Runtime.dll"));
            Type type = native.GetType("Character3DDisplayManager", true);
            go = new GameObject("actual native private-field binding witness");
            Component manager = go.AddComponent(type);
            FieldInfo field = type.GetField("isHidden", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null || field.FieldType != typeof(bool)) throw new Exception("Original native hidden field is absent");
            Assembly plugin = Assembly.LoadFile(manifest.plugin);
            Type readerType = plugin.GetType("GloomhavenVR.WorldUI.MenuDepth.MenuDepthCharacters", true);
            var reader = (Delegate)readerType.GetMethod("CreateHiddenReader", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            if (reader == null) throw new Exception("Actual plugin refused the original native private field");
            field.SetValue(manager, false);
            if ((bool)reader.DynamicInvoke(manager)) throw new Exception("Original false private field read as true");
            field.SetValue(manager, true);
            if (!(bool)reader.DynamicInvoke(manager)) throw new Exception("Original true private field read as false");
            File.WriteAllText(manifest.result, "PASS actual original GH.Runtime private field and retained actual net472 plugin: 4 assertions\n"
                + "Native assembly: " + native.Location + "\nPlugin: " + plugin.Location + "\nNo game prefab, Start/Display or XR executed.\n");
            UnityEngine.Object.DestroyImmediate(go);
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            if (go != null) UnityEngine.Object.DestroyImmediate(go);
            File.WriteAllText(manifest.result, "FAIL " + exception + "\n");
            EditorApplication.Exit(1);
        }
    }
}
