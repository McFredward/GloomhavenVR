using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class EnvironmentRunner
{
    [Serializable] private class Case { public string name, dll, expected; }
    [Serializable] private class Manifest { public string result; public Case[] cases; }
    private static Manifest manifest;
    private static bool ran;
    private static LightProbes nativeProbeFixture;
    public static void Start()
    {
        string[] args = Environment.GetCommandLineArgs();
        manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(args[Array.IndexOf(args,"-environmentManifest")+1]));
        // Produce real engine-baked coefficients/positions. Renderer probe flags
        // alone are not a populated lighting fixture and cannot prove refusal.
        var nativeGroup = new GameObject("NativeProbeFixture.Bake");
        var group = nativeGroup.AddComponent<LightProbeGroup>();
        group.probePositions = new[] { new Vector3(-2,-2,-2),new Vector3(4,-2,-2),new Vector3(-2,4,-2),new Vector3(-2,-2,4),new Vector3(4,4,4) };
        Lightmapping.Bake();
        nativeProbeFixture = LightmapSettings.lightProbes;
        if (nativeProbeFixture == null || nativeProbeFixture.count < 4) throw new Exception("Actual engine probe bake produced no usable fixture");
        nativeProbeFixture = UnityEngine.Object.Instantiate(nativeProbeFixture);
        LightmapSettings.lightProbes = null;
        UnityEngine.Object.DestroyImmediate(nativeGroup);
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        EditorApplication.update += RunWhenPlaying;
        EditorApplication.EnterPlaymode();
    }
    private static void RunWhenPlaying()
    {
        if (!EditorApplication.isPlaying || ran) return;
        ran = true; bool passed = true;
        using (var output = new StreamWriter(manifest.result))
        {
            output.WriteLine("Unity " + Application.unityVersion + "; graphics device: " + SystemInfo.graphicsDeviceName);
            Shader simple = Shader.Find("GloomhavenVR/ScenarioSimpleEnvironment");
            Shader native = Shader.Find("Amp_Basic_N_MRAO");
            if (simple == null || !simple.isSupported || ShaderUtil.ShaderHasError(simple)
                || native == null || !native.isSupported || ShaderUtil.ShaderHasError(native))
            { output.WriteLine("FAIL: production shader import/graphics support"); EditorApplication.Exit(1); return; }
            foreach (var entry in manifest.cases)
            {
                var nativeHarmony = new HarmonyLib.Harmony("ghvr.environment.fixture."+entry.name);
                var existing = new HashSet<int>();
                foreach (var obj in UnityEngine.Object.FindObjectsOfType<GameObject>(true)) existing.Add(obj.GetInstanceID());
                try
                {
                    var assembly = Assembly.LoadFile(entry.dll);
                    // Game callbacks remain explicit boundary classes. The shared
                    // Camera.FireOnPreCull postfix runs through the REAL pinned
                    // HarmonyX/MonoMod backend on Unity's managed native seam.
                    assembly.GetType("HarmonyLib.Harmony").GetField("PatchObserver",BindingFlags.Static|BindingFlags.NonPublic)
                        .SetValue(null,new Action<Type>(patch =>
                        {
                            if (patch.Name != "Camera_FinalPreCull_BudgetPatch") return;
                            nativeHarmony.UnpatchSelf();
                            var target=typeof(Camera).GetMethod("FireOnPreCull",BindingFlags.Static|BindingFlags.NonPublic);
                            var postfix=patch.GetMethod("Postfix",BindingFlags.Static|BindingFlags.NonPublic);
                            if(target==null || target.GetMethodBody()==null || postfix==null) throw new Exception("Original managed final pre-cull seam absent");
                            nativeHarmony.Patch(target,postfix:new HarmonyLib.HarmonyMethod(postfix));
                        }));
                    assembly.GetType("HarmonyLib.Harmony").GetField("UnpatchObserver",BindingFlags.Static|BindingFlags.NonPublic)
                        .SetValue(null,new Action(nativeHarmony.UnpatchSelf));
                    assembly.GetType("EnvironmentProgram").GetField("NativeProbeFixture").SetValue(null,nativeProbeFixture);
                    assembly.GetType("GloomhavenVR.Core.BankFixtureAssets").GetField("Provider",BindingFlags.Static|BindingFlags.NonPublic)
                        .SetValue(null,new Func<string,TextAsset>(path => AssetDatabase.LoadAssetAtPath<TextAsset>(path)));
                    int count = (int)assembly.GetType("EnvironmentProgram").GetMethod("Run").Invoke(null,null);
                    if (!String.IsNullOrEmpty(entry.expected)) throw new Exception("negative control escaped: " + entry.name);
                    output.WriteLine("PASS " + entry.name + ": " + count + " runtime assertions");
                }
                catch (Exception error)
                {
                    while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
                    if (!String.IsNullOrEmpty(entry.expected) && error.Message.Contains(entry.expected))
                        output.WriteLine("PASS negative control " + entry.name + ": " + error.Message);
                    else { passed = false; output.WriteLine("FAIL " + entry.name + ": " + error); }
                }
                finally
                {
                    nativeHarmony.UnpatchSelf();
                    // Failed controls must not leave another assembly's native scene roots.
                    foreach (var obj in UnityEngine.Object.FindObjectsOfType<GameObject>(true))
                        if (obj != null && !existing.Contains(obj.GetInstanceID())) UnityEngine.Object.DestroyImmediate(obj);
                    Physics.SyncTransforms();
                }
                output.Flush();
            }
        }
        EditorApplication.Exit(passed ? 0 : 1);
    }
}
