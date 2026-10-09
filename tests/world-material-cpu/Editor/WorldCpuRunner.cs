using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class WorldCpuRunner
{
    [Serializable] private class Variant { public string name,dll; }
    [Serializable] private class Manifest { public string output;public Variant[] variants;public string[] workloads;public int rounds,frames; }
    [Serializable] private class Row { public string variant,workload;public int round,position;public double elapsedMilliseconds;public long allocatedBytes;public int frames;public long monoHeapDelta;public int collections;public double[] frameMilliseconds; }
    [Serializable] private class Measurement { public double elapsedMilliseconds;public long allocatedBytes;public int frames;public long monoHeapDelta;public int collections;public double[] frameMilliseconds; }
    private sealed class Lane
    {
        internal string Name;internal Func<string,object> Create;internal Func<object,int,string> Batch;internal Action<object> Destroy;internal Func<object,string> Describe;internal Func<string> AllocationProbe;
        internal Lane(Variant variant)
        {
            Name=variant.name;var assembly=Assembly.LoadFile(variant.dll);var type=assembly.GetType("WorldMaterialCpuProgram");
            Create=(Func<string,object>)Delegate.CreateDelegate(typeof(Func<string,object>),type.GetMethod("Create"));
            Batch=(Func<object,int,string>)Delegate.CreateDelegate(typeof(Func<object,int,string>),type.GetMethod("Batch"));
            Destroy=(Action<object>)Delegate.CreateDelegate(typeof(Action<object>),type.GetMethod("Destroy"));
            Describe=(Func<object,string>)Delegate.CreateDelegate(typeof(Func<object,string>),type.GetMethod("Describe"));
            AllocationProbe=(Func<string>)Delegate.CreateDelegate(typeof(Func<string>),type.GetMethod("AllocationProbe"));
        }
    }
    private static Manifest manifest;private static bool started;
    public static void Start()
    {
        string[] args=Environment.GetCommandLineArgs();manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(args[Array.IndexOf(args,"-worldCpuManifest")+1]));
        EditorSettings.enterPlayModeOptionsEnabled=true;EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload|EnterPlayModeOptions.DisableSceneReload;
        EditorApplication.update+=Run;EditorApplication.EnterPlaymode();
    }
    private static void Run()
    {
        if(!EditorApplication.isPlaying||started)return;started=true;
        new GameObject("CPU benchmark runner").AddComponent<WorldCpuCoroutine>().StartCoroutine(Execute());
    }
    private static IEnumerator Execute()
    {
        Exception failure=null;
        Lane[] lanes=null;
        try{lanes=Array.ConvertAll(manifest.variants,v=>new Lane(v));}catch(Exception error){failure=error;}
        using(var report=new StreamWriter(manifest.output))
        {
            report.WriteLine("{\"unity\":\""+Application.unityVersion+"\",\"runtime\":\""+Environment.Version+"\",\"frequency\":"+System.Diagnostics.Stopwatch.Frequency+"}");
            foreach(Lane lane in lanes)report.WriteLine("{\"allocationProbe\":\""+lane.Name+"\",\"values\":"+lane.AllocationProbe()+"}");
            foreach(string workload in manifest.workloads)
            {
                if(failure!=null)break;
                object[] fixtures=new object[lanes.Length];
                try
                {
                    for(int i=0;i<lanes.Length;i++)fixtures[i]=lanes[i].Create(workload);
                    for(int i=0;i<lanes.Length;i++)report.WriteLine("{\"fixtureShape\":\""+lanes[i].Name+"\",\"workload\":\""+workload+"\",\"values\":"+lanes[i].Describe(fixtures[i])+"}");
                    for(int i=0;i<lanes.Length;i++)lanes[i].Batch(fixtures[i],16);
                    for(int round=0;round<manifest.rounds;round++)
                    {
                        // Alternate AB/BA, then repeat reversed to retain both
                        // orderings. Both sources execute in one editor/process.
                        for(int position=0;position<lanes.Length;position++)
                        {
                            int lane=(round&1)==0?position:lanes.Length-1-position;
                            Measurement measured=JsonUtility.FromJson<Measurement>(lanes[lane].Batch(fixtures[lane],manifest.frames));
                            report.WriteLine(JsonUtility.ToJson(new Row{variant=lanes[lane].Name,workload=workload,round=round,position=position,
                                elapsedMilliseconds=measured.elapsedMilliseconds,allocatedBytes=measured.allocatedBytes,frames=measured.frames,
                                monoHeapDelta=measured.monoHeapDelta,collections=measured.collections,frameMilliseconds=measured.frameMilliseconds}));
                        }
                    }
                }
                catch(Exception error){failure=error;report.WriteLine("FAIL "+workload+": "+error);}
                finally
                {
                    for(int i=0;i<fixtures.Length;i++)if(fixtures[i]!=null)lanes[i].Destroy(fixtures[i]);
                    report.Flush();
                }
                yield return null;
            }
        }
        if(failure!=null)Debug.LogException(failure);
        EditorApplication.Exit(failure==null?0:1);
    }
}
public sealed class WorldCpuCoroutine:MonoBehaviour { }
