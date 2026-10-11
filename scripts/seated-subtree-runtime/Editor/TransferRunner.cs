using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class TransferRunner
{
    [Serializable] private class Case { public string name,dll,expected; }
    [Serializable] private class Manifest { public string result;public Case[] cases; }
    private static Manifest manifest;
    private static StreamWriter output;
    private static IEnumerator running;
    private static Type program;
    private static int index;
    private static bool passed=true;
    private static HashSet<int> existing;
    public static void Start()
    {
        string[] args=Environment.GetCommandLineArgs();
        manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(args[Array.IndexOf(args,"-interactionManifest")+1]));
        output=new StreamWriter(manifest.result);output.WriteLine("Unity "+Application.unityVersion);
        EditorSettings.enterPlayModeOptionsEnabled=true;
        EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload|EnterPlayModeOptions.DisableSceneReload;
        EditorApplication.update+=Tick;EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if(!EditorApplication.isPlaying)return;
        if(index>=manifest.cases.Length){output.Dispose();EditorApplication.Exit(passed?0:1);return;}
        var entry=manifest.cases[index];
        try
        {
            if(running==null)
            {
                existing=new HashSet<int>();foreach(var obj in UnityEngine.Object.FindObjectsOfType<GameObject>(true))existing.Add(obj.GetInstanceID());
                program=Assembly.LoadFile(entry.dll).GetType("InteractionProgram",true);
                running=(IEnumerator)program.GetMethod("Run").Invoke(null,null);
            }
            if(running.MoveNext())return;
            if(!String.IsNullOrEmpty(entry.expected))throw new Exception("negative control escaped: "+entry.name);
            output.WriteLine("PASS "+entry.name+": "+program.GetProperty("Checks").GetValue(null)+" runtime assertions");
        }
        catch(Exception error)
        {
            while(error is TargetInvocationException&&error.InnerException!=null)error=error.InnerException;
            if(!String.IsNullOrEmpty(entry.expected)&&error.Message.Contains(entry.expected))output.WriteLine("PASS negative control "+entry.name+": "+error.Message);
            else{passed=false;output.WriteLine("FAIL "+entry.name+": "+error);}
        }
        (running as IDisposable)?.Dispose();running=null;
        foreach(var obj in UnityEngine.Object.FindObjectsOfType<GameObject>(true))if(obj!=null&&!existing.Contains(obj.GetInstanceID()))UnityEngine.Object.DestroyImmediate(obj);
        Physics.SyncTransforms();output.Flush();index++;
    }
}
