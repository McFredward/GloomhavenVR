using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class PalmRenderRunner
{
    private static bool _ran;
    public static void Start()
    {
        AssetDatabase.importPackageCompleted += Imported;
        var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.textmeshpro/Scripts/Runtime/TMP_Text.cs");
        AssetDatabase.ImportPackage(System.IO.Path.Combine(package.resolvedPath, "Package Resources/TMP Essential Resources.unitypackage"), false);
    }
    private static void Imported(string packageName)
    {
        AssetDatabase.importPackageCompleted -= Imported;
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        EditorApplication.update += RenderWhenPlaying;
        EditorApplication.EnterPlaymode();
    }
    private static void RenderWhenPlaying()
    {
        if (!EditorApplication.isPlaying || _ran) return;
        _ran = true;
        try
        {
            var args = Environment.GetCommandLineArgs();
            var dll = args[Array.IndexOf(args, "-fixtureDll") + 1];
            Assembly.LoadFile(dll).GetType("PalmRenderProgram").GetMethod("Run").Invoke(null, null);
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }
}
