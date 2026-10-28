using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class EnvironmentScopeRunner
{
    [Serializable] private sealed class Case { public string name,dll,expected; }
    [Serializable] private sealed class Manifest { public string result,evidence;public Case[] cases; }
    private static Manifest manifest; private static StreamWriter writer; private static IEnumerator pending;
    private static Type program; private static Case active; private static int index,count; private static bool passed=true;
    public static void Start()
    {
        string[] args=Environment.GetCommandLineArgs();
        manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(args[Array.IndexOf(args,"-environmentScopeManifest")+1]));
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        EditorSettings.enterPlayModeOptionsEnabled=true;
        EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload|EnterPlayModeOptions.DisableSceneReload;
        writer=new StreamWriter(manifest.result);writer.WriteLine("Unity "+Application.unityVersion+"; "+SystemInfo.graphicsDeviceType+"; "+SystemInfo.graphicsDeviceName);
        EditorApplication.update+=Tick;EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if(!EditorApplication.isPlaying)return;
        while(pending!=null||index<manifest.cases.Length)
        {
            try
            {
                if(pending==null)
                {
                    active=manifest.cases[index++];program=Assembly.LoadFile(active.dll).GetType("EnvironmentScopeProgram");
                    string output=Path.Combine(manifest.evidence,active.name);Directory.CreateDirectory(output);
                    pending=(IEnumerator)program.GetMethod("Run").Invoke(null,new object[]{output});count=-1;
                }
                if(pending.MoveNext()){if(pending.Current is int result)count=result;return;}
                if(!String.IsNullOrEmpty(active.expected))throw new Exception("Negative control escaped: "+active.name);
                if(count<0)throw new Exception("Missing assertion count");writer.WriteLine("PASS "+active.name+": "+count+" actual Unity environment scope assertions");
            }
            catch(Exception error)
            {
                while(error is TargetInvocationException&&error.InnerException!=null)error=error.InnerException;
                if(!String.IsNullOrEmpty(active.expected)&&error.Message.Contains(active.expected))writer.WriteLine("PASS negative control "+active.name+": "+error.Message);
                else {passed=false;writer.WriteLine("FAIL "+active.name+": "+error);}
            }
            try{(pending as IDisposable)?.Dispose();program?.GetMethod("Cleanup").Invoke(null,null);}
            catch(Exception error){passed=false;writer.WriteLine("FAIL cleanup: "+error);}
            pending=null;program=null;writer.Flush();return;
        }
        writer.Dispose();EditorApplication.update-=Tick;EditorApplication.Exit(passed?0:1);
    }
}
