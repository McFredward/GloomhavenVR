using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
public static class DepthRunner
{
    [Serializable] private class Case {public string name,dll,expected;}
    [Serializable] private class Manifest {public string result,evidence;public Case[] cases;}
    private static Manifest manifest;private static bool ran;
    public static void Start()
    {
        string[] args=Environment.GetCommandLineArgs();manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(args[Array.IndexOf(args,"-depthManifest")+1]));
        EditorSettings.enterPlayModeOptionsEnabled=true;EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload|EnterPlayModeOptions.DisableSceneReload;EditorApplication.update+=Run;EditorApplication.EnterPlaymode();
    }
    private static void Run()
    {
        if(!EditorApplication.isPlaying||ran)return;ran=true;bool pass=true;
        using(var output=new StreamWriter(manifest.result))
        {
            output.WriteLine("Unity "+Application.unityVersion+"; "+SystemInfo.graphicsDeviceName);
            foreach(var entry in manifest.cases)
            {
                try
                {
                    string evidence=Path.Combine(manifest.evidence,entry.name);Directory.CreateDirectory(evidence);
                    int count=(int)Assembly.LoadFile(entry.dll).GetType("ActorBarDepthProgram").GetMethod("Run").Invoke(null,new object[]{evidence});
                    if(!String.IsNullOrEmpty(entry.expected))throw new Exception("negative control escaped: "+entry.name);
                    output.WriteLine("PASS "+entry.name+": "+count+" runtime/pixel assertions");
                }
                catch(Exception ex)
                {
                    while(ex is TargetInvocationException&&ex.InnerException!=null)ex=ex.InnerException;
                    if(!String.IsNullOrEmpty(entry.expected)&&ex.Message.Contains(entry.expected))output.WriteLine("PASS negative control "+entry.name+": "+ex.Message);
                    else{pass=false;output.WriteLine("FAIL "+entry.name+": "+ex);}
                }
                finally{foreach(var obj in UnityEngine.Object.FindObjectsOfType<GameObject>())UnityEngine.Object.DestroyImmediate(obj);}
                output.Flush();
            }
        }
        EditorApplication.Exit(pass?0:1);
    }
}
