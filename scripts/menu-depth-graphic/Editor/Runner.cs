using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class MenuDepthGraphicRunner
{
    [Serializable] private sealed class Case { public string name, dll, expected, method; }
    [Serializable] private sealed class Manifest { public string result, evidence, gameManaged, harmonyRuntime, nativeTrimBundle; public Case[] cases; }
    private static Manifest manifest;
    private static bool ran;
    public static void Start()
    {
        Debug.Log("MENU DEPTH GRAPHIC FIXTURE START: executeMethod entered");
        string[] args = Environment.GetCommandLineArgs();
        manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(args[Array.IndexOf(args, "-menuDepthGraphicManifest") + 1]));
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorApplication.update += Run;
        File.WriteAllText(Path.Combine(manifest.evidence, "execute-method-start.txt"), "executeMethod entered; update registered.\n");
    }
    private static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        string name = new AssemblyName(args.Name).Name + ".dll";
        foreach (string folder in new[] { manifest.gameManaged, manifest.harmonyRuntime })
        {
            string path = Path.Combine(folder, name);
            if (File.Exists(path)) return Assembly.LoadFrom(path);
        }
        return null;
    }
    private static Sprite MakeNativeCrop()
    {
        var texture = new Texture2D(128, 128, TextureFormat.RGBA32, false) { name = "original-native-subrect-texture" };
        var pixels = new Color32[128 * 128];
        for (int y = 24; y < 104; y++) for (int x = 16; x < 88; x++)
            pixels[y * 128 + x] = x % 12 <= 1 || y % 12 <= 1
                ? new Color32(20, 20, 20, 255)
                : new Color32((byte)(70 + x), (byte)(80 + y), (byte)(220 - x), 255);
        texture.SetPixels32(pixels); texture.Apply();
        Sprite sprite = Sprite.Create(texture, new Rect(16, 24, 72, 80), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
        sprite.name = "original-native-subrect";
        File.WriteAllText(Path.Combine(manifest.evidence, "native-subrect-sprite.txt"),
            "Actual Unity Sprite.Create subrect and native uGUI vertex/UV generation.\n"
            + "This bounds full-texture crop/UV mapping, not publisher fractional trim metadata or atlas packing.\nlogical="
            + sprite.rect + "\ntexture=" + sprite.texture.width + "x" + sprite.texture.height + "\n");
        return sprite;
    }
    private static void Run()
    {
        if (ran) return;
        ran = true; bool pass = true;
        using (var writer = new StreamWriter(manifest.result))
        {
            writer.WriteLine("Unity " + Application.unityVersion + "; " + SystemInfo.graphicsDeviceName + "; " + SystemInfo.graphicsDeviceType);
            writer.Flush(); Debug.Log("MENU DEPTH GRAPHIC FIXTURE: creating native sprite witnesses");
            Sprite sprite;
            try { sprite = MakeNativeCrop(); }
            catch (Exception error) { writer.WriteLine("FAIL fixture: " + error); EditorApplication.Exit(1); return; }
            AssetBundle nativeBundle = null;
            Sprite nativeTrim = null;
            foreach (Case item in manifest.cases)
                if (item.method == "NativeMip" && nativeBundle == null)
                {
                    Debug.Log("MENU DEPTH GRAPHIC FIXTURE: loading publisher trim bundle " + manifest.nativeTrimBundle);
                    nativeBundle = AssetBundle.LoadFromFile(manifest.nativeTrimBundle);
                    if (nativeBundle == null) throw new Exception("Actual native trim bundle failed to load.");
                    Debug.Log("MENU DEPTH GRAPHIC FIXTURE: loading publisher trim sprites");
                    foreach (Sprite candidate in nativeBundle.LoadAllAssets<Sprite>())
                        if (candidate.name == "CR_DF_03_Demon") nativeTrim = candidate;
                    if (nativeTrim == null) throw new Exception("Actual native CR_DF_03_Demon sprite is absent.");
                    Debug.Log("MENU DEPTH GRAPHIC FIXTURE: publisher trim sprite ready");
                }
            foreach (Case item in manifest.cases)
            {
                Assembly assembly = null;
                try
                {
                    string output = Path.Combine(manifest.evidence, item.name); Directory.CreateDirectory(output);
                    assembly = Assembly.LoadFile(item.dll);
                    Debug.Log("MENU DEPTH GRAPHIC FIXTURE: executing " + item.name);
                    int count = (int)assembly.GetType("MenuDepthGraphicProgram").GetMethod(item.method)
                        .Invoke(null, new object[] { output, item.method == "NativeMip" ? nativeTrim : sprite });
                    if (!string.IsNullOrEmpty(item.expected)) throw new Exception("Negative control escaped: " + item.name);
                    writer.WriteLine("PASS " + item.name + ": " + count + " assertions");
                }
                catch (Exception error)
                {
                    while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
                    if (!string.IsNullOrEmpty(item.expected) && error.Message.Contains(item.expected))
                        writer.WriteLine("PASS negative control " + item.name + ": " + error.Message);
                    else { pass = false; writer.WriteLine("FAIL " + item.name + ": " + error); }
                }
                finally
                {
                    if (assembly != null)
                    {
                        var messages = (IEnumerable)assembly.GetType("GloomhavenVR.Core.VRLog")
                            .GetField("Messages", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                        using (var log = new StreamWriter(Path.Combine(manifest.evidence, item.name, "lifecycle.log")))
                            foreach (object message in messages) log.WriteLine(message);
                        Type driver = assembly.GetType("GloomhavenVR.WorldUI.MenuDepth.MenuDepthDriver");
                        driver.GetMethod("Shutdown", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
                    }
                    foreach (GameObject obj in UnityEngine.Object.FindObjectsOfType<GameObject>()) UnityEngine.Object.DestroyImmediate(obj);
                    if (assembly != null)
                        assembly.GetType("GloomhavenVR.WorldUI.MenuDepth.MenuDepthDisplay")
                            .GetMethod("Shutdown", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
                }
                writer.Flush();
            }
            if (nativeBundle != null) nativeBundle.Unload(true);
        }
        EditorApplication.Exit(pass ? 0 : 1);
    }
}
