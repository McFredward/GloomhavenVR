using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Run actual evaluated native animation and render frames for the actor bar height proof.
public static class PoseRunner
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
                program=Array.Find(AppDomain.CurrentDomain.GetAssemblies(), a=>a.GetName().Name==Path.GetFileNameWithoutExtension(entry.dll)).GetType("InteractionProgram");
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
    private static void Finish()
    {
        foreach(var go in UnityEngine.Object.FindObjectsOfType<GameObject>(true))
            if(go!=null&&!existing.Contains(go.GetInstanceID()))UnityEngine.Object.DestroyImmediate(go);
        foreach(var bundle in new List<AssetBundle>(AssetBundle.GetAllLoadedAssetBundles()))bundle.Unload(true);
        Physics.SyncTransforms();output.Flush();steps=null;program=null;current++;
    }
}
