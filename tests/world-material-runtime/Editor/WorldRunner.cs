using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class WorldRunner
{
    [Serializable]private class Case {public string name,dll,expected;}
    [Serializable]private class Manifest {public string result;public Case[] cases;}
    private static Manifest manifest;private static bool ran;
    public static void Start()
    {
        string[] args=Environment.GetCommandLineArgs();manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(args[Array.IndexOf(args,"-worldManifest")+1]));
        EditorSettings.enterPlayModeOptionsEnabled=true;EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload|EnterPlayModeOptions.DisableSceneReload;
        EditorApplication.update+=Run;EditorApplication.EnterPlaymode();
    }
    private static void Run()
    {
        if(!EditorApplication.isPlaying||ran)return;ran=true;bool passed=true;
        using(var report=new StreamWriter(manifest.result))
        {
            report.WriteLine("Unity "+Application.unityVersion+"; GPU "+SystemInfo.graphicsDeviceName);
            foreach(var entry in manifest.cases)
            {
                var previous=new HashSet<int>();foreach(var go in UnityEngine.Object.FindObjectsOfType<GameObject>(true))previous.Add(go.GetInstanceID());
                Assembly assembly=null;
                try
                {
                    assembly=Assembly.LoadFile(entry.dll);int assertions=(int)assembly.GetType("WorldMaterialProgram").GetMethod("Run").Invoke(null,null);
                    if(!String.IsNullOrEmpty(entry.expected))throw new Exception("negative control escaped");
                    report.WriteLine("PASS "+entry.name+": "+assertions+" runtime assertions");
                }
                catch(Exception error)
                {
                    while(error is TargetInvocationException&&error.InnerException!=null)error=error.InnerException;
                    if(!String.IsNullOrEmpty(entry.expected)&&error.Message.Contains(entry.expected))report.WriteLine("PASS negative "+entry.name+": "+error.Message);
                    else{passed=false;report.WriteLine("FAIL "+entry.name+": "+error);}
                }
                finally
                {
                    if(assembly!=null)
                    {
                        var budget=assembly.GetType("GloomhavenVR.Core.WorldMaterialBudget");budget.GetMethod("Shutdown",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,null);
                        var session=assembly.GetType("GloomhavenVR.Core.VRSession");object harmony=session.GetField("Harmony",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
                        harmony.GetType().GetMethod("UnpatchSelf").Invoke(harmony,null);
                    }
                    foreach(var go in UnityEngine.Object.FindObjectsOfType<GameObject>(true))if(go!=null&&!previous.Contains(go.GetInstanceID()))UnityEngine.Object.DestroyImmediate(go);
                    Physics.SyncTransforms();
                }
                report.Flush();
            }
        }
        EditorApplication.Exit(passed?0:1);
    }
}
