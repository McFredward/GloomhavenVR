using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class ConfirmationRunner
{
 [Serializable] private sealed class Case { public string name,dll,expected; }
 [Serializable] private sealed class Manifest { public string result; public Case[] cases; }
 private static Manifest manifest;
 private static bool ran;
 public static void Start()
 {
  string[] args=Environment.GetCommandLineArgs();
  manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(args[Array.IndexOf(args,"-confirmationManifest")+1]));
  EditorSettings.enterPlayModeOptionsEnabled=true;
  EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload|EnterPlayModeOptions.DisableSceneReload;
  EditorApplication.update+=Tick;EditorApplication.EnterPlaymode();
 }
 private static void Tick()
 {
  if(!EditorApplication.isPlaying||ran) return;ran=true;
  bool passed=true;
  using(var output=new StreamWriter(manifest.result))
  {
   output.WriteLine("Unity "+Application.unityVersion+" native map confirmation lifecycle");
   foreach(var entry in manifest.cases)
   {
    try
    {
     int count=(int)Assembly.LoadFile(entry.dll).GetType("ConfirmationProgram").GetMethod("Run").Invoke(null,new object[]{entry.name});
     if(!String.IsNullOrEmpty(entry.expected)) throw new Exception("negative control escaped: "+entry.name);
     output.WriteLine("PASS "+entry.name+": "+count+" native runtime assertions");
    }
    catch(Exception error)
    {
     while(error is TargetInvocationException&&error.InnerException!=null) error=error.InnerException;
     if(!String.IsNullOrEmpty(entry.expected)&&error.Message.Contains(entry.expected)) output.WriteLine("PASS causal old-source control "+entry.name+": "+error.Message);
     else {passed=false;output.WriteLine("FAIL "+entry.name+": "+error);}
    }
    finally {foreach(var obj in UnityEngine.Object.FindObjectsOfType<GameObject>()) UnityEngine.Object.DestroyImmediate(obj);}
    output.Flush();
   }
  }
  EditorApplication.Exit(passed?0:1);
 }
}
