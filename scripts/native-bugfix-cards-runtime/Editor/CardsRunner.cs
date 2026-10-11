using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using HarmonyLib;
using UnityEditor;
using UnityEngine;

// Run actual publisher UI methods and original Unity widgets for the extra-turn restore proof.
public static class CardsRunner
{
    [Serializable] private class Case { public string name,dll,expected; }
    [Serializable] private class Manifest { public string result; public Case[] cases; }
    private static Manifest manifest;
    private static StreamWriter output;
    private static int current;
    private static int frame=-1;
    private static bool failed;
    private static IEnumerator steps;
    private static Type program;
    private static HashSet<int> existing;
    public static void Start()
    {
        string[] args=Environment.GetCommandLineArgs();
        manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(args[Array.IndexOf(args,"-interactionManifest")+1]));
        AppDomain.CurrentDomain.AssemblyResolve += (sender, entry) => {
            string name=new AssemblyName(entry.Name).Name;
            string file=Path.Combine(args[Array.IndexOf(args,"-nativeGameManaged")+1],name+".dll");
            if(!File.Exists(file)) file=Path.Combine(Path.GetDirectoryName(args[Array.IndexOf(args,"-nativeHarmony")+1]),name+".dll");
            return File.Exists(file) ? Assembly.LoadFrom(file) : null;
        };
        Assembly.LoadFrom(Path.Combine(args[Array.IndexOf(args,"-nativeGameManaged")+1],"GH.Runtime.dll"));
        // Player-built InputSystem omits its Editor buffer. Isolate the Editor-only
        // callback; actual game types, Animator evaluation and runtime callbacks remain.
        var inputs = Assembly.Load("Unity.InputSystem").GetType("UnityEngine.InputSystem.InputManager");
        var editorBoundary = new Harmony("ghvr.nativeActorAudit.editorBoundary");
        editorBoundary.Patch(AccessTools.Method(inputs,"OnUpdate"),
            prefix:new HarmonyMethod(typeof(CardsRunner),nameof(PlayerInputOnly)));
        // Importing the publisher DLL registers unrelated player bootstrap callbacks,
        // whose full-game Resources/shaders are absent in this actor-only project. Do
        // not bootstrap its debugger/outline camera. Actual actor scripts still bind.
        foreach (string boot in new[] {"EPOOutline.OutlineEffect:InitMaterials",
            "SRDebugger.AutoInitialize:OnLoadBeforeScene", "SRDebugger.AutoInitialize:OnLoad"})
        {
            string[] parts=boot.Split(':');
            MethodInfo method=AccessTools.Method(AccessTools.TypeByName(parts[0]),parts[1]);
            if(method!=null) editorBoundary.Patch(method,prefix:new HarmonyMethod(typeof(CardsRunner),nameof(ActorFixtureOnly)));
        }
        output=new StreamWriter(manifest.result);output.WriteLine("Unity "+Application.unityVersion);output.Flush();
        EditorSettings.enterPlayModeOptionsEnabled=true;
        EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload|EnterPlayModeOptions.DisableSceneReload;
        Application.targetFrameRate=120;Time.fixedDeltaTime=1f/60f;
        EditorApplication.update+=Tick;EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if(!EditorApplication.isPlaying || Time.frameCount==frame)return;
        frame=Time.frameCount;
        if(current>=manifest.cases.Length) { output.Dispose();EditorApplication.Exit(failed?1:0);return; }
        Case entry=manifest.cases[current];
        try
        {
            if(steps==null)
            {
                existing=new HashSet<int>();
                foreach(var go in UnityEngine.Object.FindObjectsOfType<GameObject>(true))existing.Add(go.GetInstanceID());
                string assemblyName=Path.GetFileNameWithoutExtension(entry.dll);
                program=(AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a=>a.GetName().Name==assemblyName)
                    ?? Assembly.LoadFrom(entry.dll)).GetType("InteractionProgram");
                steps=(IEnumerator)program.GetMethod("Run").Invoke(null,null);
            }
            if(steps.MoveNext())return;
            if(!String.IsNullOrEmpty(entry.expected))throw new Exception("negative control escaped: "+entry.name);
            output.WriteLine("PASS "+entry.name+": "+program.GetField("Checks").GetValue(null)+" runtime assertions; "+program.GetField("Metrics").GetValue(null));
            Finish();
        }
        catch(Exception error)
        {
            while(error is TargetInvocationException && error.InnerException!=null)error=error.InnerException;
            if(!String.IsNullOrEmpty(entry.expected)&&error.Message.Contains(entry.expected))
                output.WriteLine("PASS negative control "+entry.name+": "+error.Message);
            else {failed=true;output.WriteLine("FAIL "+entry.name+": "+error);}
            Finish();
        }
    }
    private static bool PlayerInputOnly(object __0) => __0.ToString() != "Editor";
    private static bool ActorFixtureOnly() => false;
    private static void Finish()
    {
        // A failing causal control must not leave its prefix installed for the
        // next independent case in the same real Unity process.
        new Harmony("ghvr.bugfixCards.candidate").UnpatchSelf();
        new Harmony("ghvr.bugfixCards.nativeBoundary").UnpatchSelf();
        foreach(var go in UnityEngine.Object.FindObjectsOfType<GameObject>(true))
            if(go!=null&&!existing.Contains(go.GetInstanceID()))UnityEngine.Object.DestroyImmediate(go);
        foreach(var bundle in new List<AssetBundle>(AssetBundle.GetAllLoadedAssetBundles()))bundle.Unload(true);
        Physics.SyncTransforms();output.Flush();steps=null;program=null;current++;
    }
}
