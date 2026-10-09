using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class WallPerformanceRunner
{
    [Serializable]private class Case{public string name,dll,expected;}
    [Serializable]private class Manifest{public string result;public Case[] cases;}
    private static Manifest manifest;private static bool ran;
    public static void Start()
    {var args=Environment.GetCommandLineArgs();manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(args[Array.IndexOf(args,"-wallManifest")+1]));EditorSettings.enterPlayModeOptionsEnabled=true;EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload|EnterPlayModeOptions.DisableSceneReload;EditorApplication.update+=Run;EditorApplication.EnterPlaymode();}
    private static void Run()
    {
        if(ran||!EditorApplication.isPlaying)return;ran=true;bool passed=true;var timings=new List<object>();Assembly production=null,baseline=null;
        using(var report=new StreamWriter(manifest.result))
        {
            report.WriteLine("Unity "+Application.unityVersion+"; GPU "+SystemInfo.graphicsDeviceName);
            foreach(var item in manifest.cases)
            {
                var before=new HashSet<int>();foreach(var obj in UnityEngine.Object.FindObjectsOfType<GameObject>(true))before.Add(obj.GetInstanceID());
                Assembly asm=null;
                try
                {
                    asm=Assembly.LoadFile(item.dll);if(item.name=="baseline-entry"){baseline=asm;report.WriteLine("BASELINE entry timing only");continue;}
                    if(item.name=="production")production=asm;
                    int assertions=(int)asm.GetType("WallPerformanceProgram").GetMethod("Run").Invoke(null,null);
                    if(!String.IsNullOrEmpty(item.expected))throw new Exception("negative control escaped");
                    report.WriteLine("PASS "+item.name+": "+assertions+" assertions");
                }
                catch(Exception error)
                {
                    while(error is TargetInvocationException&&error.InnerException!=null)error=error.InnerException;
                    if(!String.IsNullOrEmpty(item.expected)&&error.Message.Contains(item.expected))report.WriteLine("PASS negative "+item.name+": "+error.Message);
                    else{passed=false;report.WriteLine("FAIL "+item.name+": "+error);}
                }
                finally
                {
                    if(asm!=null)report.WriteLine((string)asm.GetType("WallPerformanceProgram").GetMethod("Messages").Invoke(null,null));
                    foreach(var obj in UnityEngine.Object.FindObjectsOfType<GameObject>(true))if(obj!=null&&!before.Contains(obj.GetInstanceID()))UnityEngine.Object.DestroyImmediate(obj);
                    // The native scene handles remain actual engine handles. Synchronous
                    // editor cleanup is enough here because no coroutine/scene load runs.
                    // Native scenes persist until this isolated Unity process exits; all test GameObjects are synchronously removed.
                    report.Flush();
                }
            }
            if(passed&&production!=null&&baseline!=null)
            {
                for(int round=0;round<15;round++)foreach(int mode in new[]{0,2,1})
                {
                    var values=new Dictionary<string,double>();foreach(string label in round%2==0?new[]{"baseline","candidate"}:new[]{"candidate","baseline"})
                    {Assembly asm=label=="baseline"?baseline:production;values[label]=(double)asm.GetType("WallPerformanceProgram").GetMethod("TimeEntry").Invoke(null,new object[]{mode,30000});}
                    timings.Add(new Dictionary<string,object>{{"round",round},{"mode",mode},{"baselineMs",values["baseline"]},{"candidateMs",values["candidate"]}});
                    // Native scenes persist until this isolated Unity process exits; all test GameObjects are synchronously removed.
                }
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(manifest.result),"entry-timings.json"),SerializeTimings(timings));
            }
        }
        EditorApplication.Exit(passed?0:1);
    }
    private static string SerializeTimings(List<object> rows)
    {var lines=new List<string>();foreach(Dictionary<string,object> row in rows)lines.Add("{\"round\":"+row["round"]+",\"mode\":"+row["mode"]+",\"baselineMs\":"+((double)row["baselineMs"]).ToString("R",System.Globalization.CultureInfo.InvariantCulture)+",\"candidateMs\":"+((double)row["candidateMs"]).ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"}");return "[\n"+String.Join(",\n",lines)+"\n]\n";}
}
