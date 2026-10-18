using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GloomhavenVR.Core;
using M=GloomhavenVR.Core.SteamLink.SteamLinkCompositorMonitor;

internal static class Program
{
    private static int checks;
    private const string Ready="{\"schema\":\"sfnp-pc-compositor-state-v1\",\"event\":\"ready\",\"read_only\":true,\"non_atomic\":true}";
    private static void Check(bool value,string label){checks++;if(!value)throw new InvalidOperationException(label);}
    private static async Task Until(Func<bool> condition,string label)
    {for(int i=0;i<1000&&!condition();i++)await Task.Delay(2);Check(condition(),label);}
    private static string Sample(uint number=1,Action<Dictionary<string,object>>? edit=null)
    {
        var data=new Dictionary<string,object>{{"schema","sfnp-pc-compositor-state-v1"},{"event","sample"},{"sample",number},{"begin_ms",10ul},{"end_ms",11ul},{"scene_focus_pid",20u},{"last_frame_renderer_pid",20u}};
        foreach(string key in "fg_r fg_g fg_b fg_a bg_r bg_g bg_b bg_a grid_alpha scene_dimming".Split(' '))data[key]=0.5f;
        foreach(string key in "numeric_valid dashboard_visible can_render_scene do_not_fade_to_grid force_fade_tracking standalone_system_layer hmd_connected identity_stable identity_valid".Split(' '))data[key]=true;
        foreach(string key in "do_not_fade_to_grid_error force_fade_tracking_error scene_dimming_error standalone_system_layer_error hmd_class frame_link_profile tracking_error model_error registration_error".Split(' '))data[key]=0;
        data["frame_link_profile"]=1;edit?.Invoke(data);return JsonSerializer.Serialize(data);
    }
    private static string Event(string name,uint samples=0)=>JsonSerializer.Serialize(new Dictionary<string,object>{{"schema","sfnp-pc-compositor-state-v1"},{"event",name},{"samples",samples},{"bytes",1000}});
    private static void Protocol()
    {
        Check(Sample().Length<1024,"Actual full public snapshot fits the line cap");
        Check(M.Decode(Sample(),out string canonical,out string kind,out uint n,out _)&&kind=="sample"&&n==1,"Exact finite native scalar snapshot admitted");
        Check(M.Decode(Sample(edit:d=>d["scene_dimming_error"]=5),out canonical,out _,out _,out _)&&canonical.Contains("\"scene_dimming\":null"),"Unavailable setting is null, never invented zero");
        Check(M.Decode(Sample(edit:d=>d["numeric_valid"]=false),out canonical,out _,out _,out _)&&canonical.Contains("\"fg_r\":null")&&canonical.Contains("\"grid_alpha\":null"),"Invalid native numerics are explicitly unavailable");
        foreach(string bad in new[]{Sample(edit:d=>d["extra"]=1),Sample(edit:d=>d["sample"]=0),Sample(edit:d=>d["sample"]=33),Sample(edit:d=>d["sample"]="1"),Sample(edit:d=>d["fg_a"]="NaN"),Sample(edit:d=>d["frame_link_profile"]=3),Sample(edit:d=>d["end_ms"]=9),Sample().Replace("\"fg_r\":0.5","\"fg_r\":1e999"),Sample()+"x",new string('x',1024),Sample().Replace("\"sample\":1","\"sample\":1,\"sample\":1"),Ready.Replace("true","false")})
            Check(!M.Decode(bad,out _,out _,out _,out _),"Untrusted malformed/unknown/overflow/duplicate observation refused");
        var p=new M.Protocol();Check(!p.Accept(Sample(),out _,out _),"Native snapshot cannot precede read-only ready identity");
        Check(p.Accept(Ready,out _,out _)&&!p.Accept(Ready,out _,out _),"Ready accepted once");
        Check(!p.Accept(Sample(2),out _,out _),"Sequence gaps refused");
        for(uint i=1;i<=32;i++)Check(p.Accept(Sample(i),out _,out _),"Bounded sequential native snapshot"+i);
        Check(p.Accept(Event("budget",32),out _,out _),"Explicit native API budget exhaustion admitted");
        Check(!p.Accept(Sample(32),out _,out _),"No sample may follow spent budget");
        Check(p.Accept(Event("stopped",32).Replace(",\"bytes\":1000",""),out _,out _),"Cooperative EOF receipt accepted");
    }
    private sealed class Reader:TextReader
    {
        private readonly object gate=new();private readonly Queue<string> parts=new();private string current="";private int cursor;private bool ended;
        private TaskCompletionSource<bool> changed=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Push(string line){lock(gate){parts.Enqueue(line);changed.TrySetResult(true);}}
        internal void End(){lock(gate){ended=true;changed.TrySetResult(true);}}
        public override async Task<int> ReadAsync(char[] buffer,int index,int count)
        {
            for(;;)
            {
                Task pending;
                lock(gate)
                {
                    if(cursor==current.Length&&parts.Count>0){current=parts.Dequeue();cursor=0;}
                    if(cursor<current.Length){int n=Math.Min(count,current.Length-cursor);current.CopyTo(cursor,buffer,index,n);cursor+=n;return n;}
                    if(ended)return 0;
                    if(changed.Task.IsCompleted)changed=new(TaskCreationOptions.RunContinuationsAsynchronously);
                    pending=changed.Task;
                }
                await pending;
            }
        }
    }
    private sealed class Child:M.IChild
    {
        internal readonly Reader Reader=new();private readonly TaskCompletionSource<bool> exit=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<bool> stop=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool AutoStop=true;internal int Stops;
        public TextReader Output=>Reader;
        public Task Exited=>exit.Task;
        public Task<bool> StopAsync(){Interlocked.Increment(ref Stops);Reader.End();if(AutoStop)Finish();return stop.Task;}
        internal void Finish(bool safe=true){Reader.End();exit.TrySetResult(true);stop.TrySetResult(safe);}
    }
    private static async Task Lifecycle()
    {
        VRLog.WantsDebug=false;M.Tick(true);M.Reset();Check(GloomhavenVR.Core.SteamLink.SteamLinkConnection.Preparations==0&&VRLog.Logs==0,"Ordinary users never prepare/query/log optional compositor observations");
        foreach(bool debug in new[]{false,true})foreach(bool requested in new[]{false,true})Check(M.Eligible(debug,requested)==(debug&&requested),"Debug and real MR eligibility are both mandatory");
        var lines=new List<string>();int starts=0;var child=new Child();
        var m=new M.Monitor(()=>{Interlocked.Increment(ref starts);return Task.FromResult<M.IChild>(child);},s=>{lock(lines)lines.Add(s);});
        m.Tick(false);m.Tick(false);Check(starts==0,"Disabled monitor launches nothing");m.Tick(true);
        await Until(()=>m.Current==M.Phase.Observing,"Worker enters observation");
        child.Reader.Push(Ready+"\n");for(uint i=1;i<=32;i++)child.Reader.Push(Sample(i)+"\n");child.Reader.Push(Event("budget",32)+"\n");
        await Until(()=>m.Current==M.Phase.Spent,"Native budget idles without stopping MR or starting another observer");
        lock(lines)Check(lines.Count==32&&lines.Sum(s=>s.Length+1)<=32768,"At most32 bounded snapshots and32KiB logged");
        for(int i=0;i<20;i++)m.Tick(true);Check(starts==1&&child.Stops==0,"Spent live helper has no per-tick restart or cutoff");
        m.Tick(false);await m.Settled;Check(child.Stops==1,"Off requests cooperative EOF exactly once");
        for(int i=0;i<10;i++)m.Tick(false);Check(starts==1,"Off remains idle");
        var failure=new M.Monitor(()=>{Interlocked.Increment(ref starts);throw new IOException();},_=>{});failure.Tick(true);await failure.Settled;
        int afterFailure=starts;for(int i=0;i<50;i++){failure.Tick(true);await Task.Delay(2);}Check(starts==afterFailure,"Start failure is not retried every Tick");
        var pending=new TaskCompletionSource<M.IChild>(TaskCreationOptions.RunContinuationsAsynchronously);var late=new Child{AutoStop=false};int launches=0;var lateLogs=new List<string>();
        var slow=new M.Monitor(()=>{Interlocked.Increment(ref launches);return pending.Task;},s=>lateLogs.Add(s));
        slow.Tick(true);await Until(()=>launches==1,"Pending old generation started");slow.Reset();slow.Tick(true);Check(launches==1,"Reset cannot overlap pending helper acquisition");
        pending.SetResult(late);await Until(()=>late.Stops==1,"Late old helper receives EOF before it can observe");
        slow.Tick(true);Check(launches==1&&lateLogs.Count==0,"Late helper cannot log or overlap a new generation during teardown");
        late.Finish();await slow.Settled;
        var next=new Child(); // The test uses a fresh owner for the next availability epoch.
        var e=new M.Monitor(()=>Task.FromResult<M.IChild>(next),_=>{});e.Tick(true);await Until(()=>e.Current==M.Phase.Observing,"Fresh eligible epoch starts");next.Reader.End();await e.Settled;Check(next.Stops==1,"Child stdout EOF also tears down ownership");
        var invalid=new Child();var logs=new List<string>();var bad=new M.Monitor(()=>Task.FromResult<M.IChild>(invalid),s=>logs.Add(s));bad.Tick(true);await Until(()=>bad.Current==M.Phase.Observing,"Malformed reader active");invalid.Reader.Push(new string('x',1025)+"\n");await bad.Settled;
        Check(invalid.Stops==1&&logs.Count==1&&!logs[0].Contains(new string('x',20)),"Overlong child data is stopped without logging arbitrary content");
        var unsafeChild=new Child();var unsafeOwner=new M.Monitor(()=>Task.FromResult<M.IChild>(unsafeChild),_=>{});unsafeOwner.Tick(true);await Until(()=>unsafeOwner.Current==M.Phase.Observing,"Unsafe teardown fixture active");unsafeChild.AutoStop=false;unsafeOwner.Reset();unsafeChild.Finish(false);await unsafeOwner.Settled;
        unsafeOwner.Reset();unsafeOwner.Tick(true);Check(unsafeOwner.Current==M.Phase.Unsafe,"Unconfirmed teardown prevents overlap even after Reset");
        slow.Reset(); // No orphan worker remains from the pending-acquisition fixture.
    }
    private static async Task Repairs()
    {
        var refused=new M.Protocol();
        Check(refused.Accept("{\"schema\":\"sfnp-pc-compositor-state-v1\",\"event\":\"refused\",\"stage\":1,\"code\":3}",out _,out _)
              && !refused.Accept(Event("stopped").Replace(",\"bytes\":1000",""),out _,out _),"Initial native refusal is terminal without an extra stopped event");
        var uncertain=new M.Monitor(()=>throw new M.OwnershipUnknownException(),_=>{});
        uncertain.Tick(true);await uncertain.Settled;uncertain.Reset();uncertain.Tick(true);
        Check(uncertain.Current==M.Phase.Unsafe,"Unknown post-start ownership stays blocked across Reset");
    }
    private static async Task Main(string[] args)
    {
        bool repair=args.Length>0&&args[0]=="--repair-only";
        if(!repair){Protocol();await Lifecycle();}
        await Repairs();
        foreach(string path in args.Skip(repair?1:0))
        {
            var protocol=new M.Protocol();int lines=0;
            foreach(string line in File.ReadAllLines(path))
            {Check(protocol.Accept(line,out _,out _),"Actual native host-fake transcript matches production parser");lines++;}
            Check(lines>0&&lines<=35,"Bounded actual native host-fake transcript, including one-line terminal refusal");
        }
        Console.WriteLine("checks="+checks+"; actual_monitor_worker=true; native_process_or_OpenVR_executed=false");
    }
}
