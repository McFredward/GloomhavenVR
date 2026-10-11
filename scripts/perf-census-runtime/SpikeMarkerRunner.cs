using System;
using System.IO;
using System.Reflection;
using System.Threading;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;

// Unlike synchronous method fixtures, this runs across actual Unity frame boundaries. It checks
// the shipped recorder reader against a real sporadic engine profiler marker, not supplied samples.
public sealed class SpikeMarkerRunner
{
    private static readonly ProfilerMarker Probe = new ProfilerMarker("Frame617.Sporadic");
    private MethodInfo roll,shutdown;
    private object marker;
    private FieldInfo sample,value;
    private string result,label;
    private bool expected,passed;
    private int ticks,assertions,lastFrame=-1;
    public static void Start(Assembly assembly,string result,bool passed,string label="production")
    {
        UnityEngine.Profiling.Profiler.enabled=true; // fixture only: activate custom positive timing proof
        IntPtr registeredHandle=Probe.Handle; string registeredName="Frame617.Sporadic"; // register before ProfilerRecorder.GetByName
        var host=new SpikeMarkerRunner();
        host.result=result;host.passed=passed;host.label=label;
        Type type=assembly.GetType("GloomhavenVR.Core.PerfSpikeDetails");
        host.roll=type.GetMethod("RollFrame",BindingFlags.NonPublic|BindingFlags.Static);
        host.shutdown=type.GetMethod("Shutdown",BindingFlags.NonPublic|BindingFlags.Static);
        var markers=(Array)type.GetField("Markers",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
        host.marker=markers.GetValue(0);
        Type markerType=host.marker.GetType();
        markerType.GetField("Name",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(host.marker,registeredName);
        host.sample=markerType.GetField("HasSample",BindingFlags.NonPublic|BindingFlags.Instance);
        host.value=markerType.GetField("Nanoseconds",BindingFlags.NonPublic|BindingFlags.Instance);
        EditorApplication.update += host.Update;
    }
    private void Update()
    {
        if(lastFrame==Time.frameCount)return;
        lastFrame=Time.frameCount;
        try
        {
            roll.Invoke(null,new object[]{true});
            // Let marker registration and the first Editor render/player-loop boundaries settle.
            if(ticks>=3)
            {
                assertions++;
                bool present=(bool)sample.GetValue(marker);
                // Unity emits a fresh zero-valued sum for a frame with no marker calls.
                // Count alone is not occurrence evidence. Positive calls must be positive;
                // absent calls must be missing or zero, never a retained positive sample.
                long ns=(long)value.GetValue(marker);
                if(expected ? !present || ns<=0 : present && ns!=0)
                    throw new Exception("real engine recorder excludes absent-frame stale samples: tick="+ticks+" expected="+expected+" present="+present+" valid="+marker.GetType().GetField("Valid",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(marker)+" fault="+marker.GetType().GetField("Fault",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(marker)+" running="+((ProfilerRecorder)marker.GetType().GetField("Recorder",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(marker)).IsRunning+" value="+value.GetValue(marker));
            }
            expected=(ticks++%2)==0;
            if(expected) using(Probe.Auto()) Thread.Sleep(2);
            if(ticks>=16)Finish(null);
        }
        catch(Exception error){Finish(error);}
    }
    private void Finish(Exception error)
    {
        EditorApplication.update -= Update;
        shutdown.Invoke(null,null);
        using(var output=new StreamWriter(result,true))
        {
            if(error==null)output.WriteLine("PASS "+label+" real engine frames: "+assertions+" runtime assertions");
            else {passed=false;output.WriteLine("FAIL "+label+" real engine frames: "+error);}
        }
        EditorApplication.Exit(passed?0:1);
    }
}
