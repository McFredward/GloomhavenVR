using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GloomhavenVR.Core;
using Q=GloomhavenVR.Core.SteamLink.SteamLinkStreamQuality;

internal static class Program
{
    private static int checks;
    private static void Check(bool value,string label){checks++;if(!value)throw new Exception(label);}
    private static async Task Until(Func<bool> value,string label)
    {
        for(int i=0;i<1000&&!value();i++)await Task.Delay(2);
        Check(value(),label);
    }
    private sealed class Lease:Q.ILease
    {
        internal readonly TaskCompletionSource<Q.ReleaseResult> Done=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<bool> Releasing=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Releases;
        public bool Applied {get;set;}=true;
        public int AppliedWidth {get;set;}=3072;
        public Task<Q.ReleaseResult> Completion=>Done.Task;
        public Task<Q.ReleaseResult> ReleaseAsync(){Interlocked.Increment(ref Releases);Releasing.TrySetResult(true);return Done.Task;}
        internal void Finish(bool restored=true,bool exited=true)=>Done.TrySetResult(new Q.ReleaseResult(restored,exited));
    }
    private static string Acquired(int requested=3072,int applied=3072,int previous=1536,int mode=0,bool auto=true)=>
        "{\"schema\":\"ghvr-steamlink-quality-v1\",\"event\":\"acquired\",\"applied\":true,\"requested_width\":"+requested+",\"applied_width\":"+applied+",\"previous_foveation_mode\":"+mode+",\"previous_sharpening\":true,\"previous_automatic_width\":"+(auto?"true":"false")+",\"previous_uniform_width\":"+previous+"}";
    private static void Protocol()
    {
        Check(Q.DecodeAcquired(Acquired(),3072),"Exact acquired protocol");
        string Absent(string line,int mask)=>line.Substring(0,line.Length-1)+",\"previous_absent_mask\":"+mask+"}";
        Check(Q.DecodeAcquired(Absent(Acquired(previous:0),1),3072),"Explicit missing unused uniform width is admitted");
        Check(Q.DecodeAcquired(Absent(Acquired(),0),3072),"Explicit full snapshot presence admitted");
        Check(!Q.DecodeAcquired(Absent(Acquired(),16),3072),"Only four absence bits exist");
        Check(!Q.DecodeAcquired(Absent(Acquired(),-1),3072),"Negative absence mask refused");
        Check(!Q.DecodeAcquired(Absent(Acquired(),1),3072),"Absent width cannot masquerade as an effective value");
        Check(!Q.DecodeAcquired(Absent(Acquired(),2),3072),"Absent auto cannot masquerade as an effective value");
        Check(!Q.DecodeAcquired(Absent(Acquired(),4),3072),"Absent sharpening cannot masquerade as an effective value");
        Check(!Q.DecodeAcquired(Absent(Acquired(applied:4608,previous:4608,mode:1,auto:false),1),3072),"Absent manual width cannot justify a larger inherited request");
        Check(!Q.DecodeAcquired(Acquired(mode:4),3072),"Original foveation mode is bounded");

        Check(!Q.DecodeAcquired(Acquired(applied:2048),3072),"A smaller driver readback cannot silently lower chosen image quality");
        Check(Q.DecodeAcquired(Acquired(applied:4608,previous:4608,mode:1,auto:false),3072),"Preserve existing higher manual no-foveation value");
        Check(Q.DecodeAcquired(Acquired(applied:3408,previous:3408,mode:3,auto:false),3072),"Preserve existing nonstep manual value");
        Check(!Q.DecodeAcquired(Acquired(applied:4608),3072),"No invented width increase");
        Check(!Q.DecodeAcquired(Acquired(applied:20000,previous:20000,mode:1,auto:false),3072),"Hard typed readback bound");
        Check(!Q.DecodeAcquired(Acquired(applied:3408,previous:3408,mode:0,auto:false),3072),"No foveated manual preservation");
        Check(!Q.DecodeAcquired(Acquired(),4096),"Wrong request refused");
        Check(!Q.DecodeAcquired(Acquired().Replace("\"applied\":true","\"applied\":true,\"applied\":true"),3072),"Duplicate key refused");
        Check(!Q.DecodeAcquired(Acquired().Replace("\"applied\":true","\"extra\":true,\"applied\":true"),3072),"Unknown key refused");
        Check(!Q.DecodeAcquired(Acquired().Replace("3072","999999999999999999"),3072),"Integer overflow refused");
        Check(!Q.DecodeAcquired(Acquired().Replace("\"applied\":true","\"applied\":\"true\""),3072),"Quoted boolean is not a typed receipt");
        Check(!Q.DecodeAcquired(Acquired().Replace("\"requested_width\":3072","\"requested_width\":\"3072\""),3072),"Quoted number is not a typed receipt");
        Check(!Q.DecodeAcquired(new string('x',2049),3072),"Bounded line refused");
        string restored="{\"schema\":\"ghvr-steamlink-quality-v1\",\"event\":\"restored\",\"ok\":true,\"conflicts\":2,\"errors\":0}";
        Check(Q.DecodeRestored(restored,out bool ok,out int conflicts)&&ok&&conflicts==2,"Owned restoration preserves external edits");
        Check(Q.DecodeRestored(restored.Replace("\"errors\":0","\"errors\":1"),out ok,out _)&&!ok,"Restoration error explicit");
        Check(!Q.DecodeRestored(restored.Replace("\"conflicts\":2","\"conflicts\":5"),out _,out _),"Only four owned keys exist");
        string error="{\"schema\":\"ghvr-steamlink-quality-v1\",\"event\":\"error\",\"error\":\"busy\",\"restored\":true,\"conflicts\":0,\"errors\":0}";
        Check(Q.DecodeError(error,out ok,out _)&&ok,"Explicit no-write/rollback error receipt");
        Check(Q.DecodeError(error.Replace("\"restored\":true","\"restored\":false"),out ok,out _)&&!ok,"Partial rollback failure remains unconfirmed");
        Check(Q.DecodeError(error.Replace("\"error\":\"busy\"","\"error\":\"abandoned\"").Replace("\"restored\":true","\"restored\":false"),out ok,out _)&&!ok,"Abandoned prior ownership is unsafe");
        foreach(int value in new[]{64,960,1024,3072,4096,4160,8192,16384})Check(Q.ValidWidth(value),"Allowed width"+value);
        foreach(int value in new[]{0,63,65,1023,1025,4097,16385})Check(!Q.ValidWidth(value),"Refused request width"+value);
        foreach(var pair in new[]{(1024,1024),(4096,4096),(2125,2176),(0,0),(int.MaxValue,0),(4040,4096),(4848,4864),(16384,16384)})Check(Q.NormalizeWidth(pair.Item1)==pair.Item2,"Bounded persisted width normalization");
    }
    private static void Gates()
    {
        Check(Q.Requested(true),"Genuine active native Link MR requested");
        Check(!Q.Requested(false),"Quality off");
        VRSession.IsRunning=false;Check(!Q.Requested(true),"No running XR");VRSession.IsRunning=true;
        FrameNativePassthrough.Required=true;Check(!Q.Requested(true),"Standalone untouched");FrameNativePassthrough.Required=false;
        GloomhavenVR.Core.SteamLink.SteamLinkConnection.Available=false;Check(!Q.Requested(true),"Other headset/transport untouched");GloomhavenVR.Core.SteamLink.SteamLinkConnection.Available=true;
        MixedReality.Enabled.Value=false;Check(!Q.Requested(true),"MR master off");MixedReality.Enabled.Value=true;
        MixedReality.SteamLinkNative.Value=false;Check(!Q.Requested(true),"Chroma-key untouched");MixedReality.SteamLinkNative.Value=true;
    }
    private static async Task StateMachine()
    {
        int calls=0;var requested=new List<int>();var logs=new List<string>();var first=new TaskCompletionSource<Q.ILease>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second=new TaskCompletionSource<Q.ILease>(TaskCreationOptions.RunContinuationsAsynchronously);
        var monitor=new Q.Monitor(width=>{lock(requested)requested.Add(width);int call=Interlocked.Increment(ref calls);return call==1?first.Task:second.Task;},(warn,text)=>{lock(logs)logs.Add((warn?"WARN ":"INFO ")+text);});
        monitor.Tick(false,3072,0);Check(calls==0,"Ineligible never schedules helper");
        monitor.Tick(true,0,0);Check(calls==0,"Unknown initial XR extent never invents a size");
        monitor.Tick(true,3072,0);await Until(()=>calls==1,"One async acquisition starts");
        for(int i=0;i<30;i++)monitor.Tick(true,3072,i*.01);
        Check(calls==1,"Menu/loading/head changes with stable eligibility do not restart acquisition");
        monitor.Reset();monitor.Tick(true,4096,1);Check(calls==1,"New epoch cannot overlap pending old acquisition");
        var old=new Lease();first.SetResult(old);await Until(()=>old.Releases==1,"Stale acquired lease cooperatively released");
        Check(monitor.Current!=Q.Phase.Active,"Stale acquired result not published active");
        for(int i=0;i<20;i++)monitor.Tick(true,4096,2+i);
        Check(calls==1,"New width waits while previous restore is unresolved");
        old.Finish();await monitor.Settled;monitor.Tick(true,4096,30);await Until(()=>calls==2,"New width starts only after old release completes");
        var active=new Lease{AppliedWidth=4096};second.SetResult(active);await Until(()=>monitor.Current==Q.Phase.Active,"Current helper acquired");
        Check(requested.Count==2&&requested[0]==3072&&requested[1]==4096,"Exact width generations");
        for(int i=0;i<20;i++)monitor.Tick(true,4096,31+i);Check(calls==2&&active.Releases==0,"Active lease has no automatic expiry");
        monitor.Tick(true,0,51);monitor.Tick(true,4096,52);
        Check(calls==2&&active.Releases==0&&monitor.Current==Q.Phase.Active,"Transient unknown XR extent retains current generation without reconfiguring loading stereo");
        monitor.Tick(false,4096,60);await Until(()=>active.Releases==1,"Explicit off requests restoration");active.Finish();await monitor.Settled;
        Check(monitor.Current==Q.Phase.Idle,"Restored lease ends idle");
        monitor.Tick(true,0,61);Check(calls==2,"Off clears permission to reuse the preceding generation's extent");
        monitor.Reset();monitor.Tick(true,0,62);Check(calls==2,"Reset with unknown extent never reuses a previous generation");

        var risky=new Lease();int riskCalls=0;var safety=new Q.Monitor(_=>{Interlocked.Increment(ref riskCalls);return Task.FromResult<Q.ILease>(risky);},(_,_)=>{});
        safety.Tick(true,3072,0);await Until(()=>safety.Current==Q.Phase.Active,"Unsafe case initially active");safety.Reset();await Until(()=>risky.Releases==1,"Unsafe case release requested");risky.Finish(false,true);await safety.Settled;
        Check(safety.Current==Q.Phase.Unavailable,"Exited but unconfirmed restore blocks baseline reuse");safety.Reset();safety.Tick(true,3072,10);Check(riskCalls==1,"Even Reset cannot adopt own un-restored value");

        var refused=new Lease{Applied=false};int refusedCalls=0;var refusal=new Q.Monitor(_=>{Interlocked.Increment(ref refusedCalls);return Task.FromResult<Q.ILease>(refused);},(_,_)=>{});
        refusal.Tick(true,3072,0);await Until(()=>refused.Releases==1,"Partially acquired refusal released");Check(refusal.Current!=Q.Phase.Active,"Refused acquisition never active");refused.Finish(false,true);await refusal.Settled;refusal.Tick(true,3072,10);Check(refusedCalls==1,"Startup rollback unknown prevents rebaseline");

        int safeRefusalWarnings=0;var safelyRefused=new Lease{Applied=false};
        var safeRefusal=new Q.Monitor(_=>Task.FromResult<Q.ILease>(safelyRefused),(warning,_)=>{if(warning)Interlocked.Increment(ref safeRefusalWarnings);});
        safeRefusal.Tick(true,3072,0);await Until(()=>safelyRefused.Releases==1,"No-write startup refusal released");safelyRefused.Finish();await safeRefusal.Settled;
        Check(safeRefusalWarnings==1,"Failed startup remains visible even when no-write rollback was confirmed");

        int failCalls=0;var failures=new Q.Monitor(_=>{Interlocked.Increment(ref failCalls);return Task.FromException<Q.ILease>(new InvalidOperationException());},(_,_)=>{});
        for(int attempt=0;attempt<3;attempt++){failures.Tick(true,3072,attempt*2);await Until(()=>failCalls==attempt+1,"Bounded failed attempt");await failures.Settled;failures.Tick(true,3072,attempt*2+.5);Check(failCalls==attempt+1,"Retry floor respected");}
        failures.Tick(true,3072,100);Check(failCalls==3&&failures.Current==Q.Phase.Unavailable,"Maximum retry count reached");

        int postStartCleanups=0;
        Q.ILease postStart=await Q.GuardStartedAsync(()=>throw new InvalidOperationException(),()=>{postStartCleanups++;return Task.FromResult(new Q.ReleaseResult(false,true));});
        Check(!postStart.Applied&&postStartCleanups==1&&!(await postStart.ReleaseAsync()).Restored,"Post-Start constructor failure retains restoration uncertainty");
        postStart=await Q.GuardStartedAsync(()=>throw new InvalidOperationException(),()=>throw new IOException());
        Check(!postStart.Applied&&!(await postStart.ReleaseAsync()).Exited,"Post-Start cleanup exception remains unsafe");
    }
    public static async Task Main(string[] args)
    {
        Protocol();Gates();await StateMachine();
        foreach(string path in args)foreach(string line in File.ReadAllLines(path))
        {
            if(line.Contains("\"event\":\"acquired\""))Check(Q.DecodeAcquired(line,3072),"Actual C acquired serializer");
            else if(line.Contains("\"event\":\"restored\""))Check(Q.DecodeRestored(line,out _,out _),"Actual C restored serializer");
            else if(line.Contains("\"event\":\"error\""))Check(Q.DecodeError(line,out _,out _),"Actual C error serializer");
            else throw new Exception("Unexpected native serializer line");
        }
        Console.WriteLine("PASS stream quality: "+checks+" checks; no OpenVR/process/settings writes executed.");
    }
}
