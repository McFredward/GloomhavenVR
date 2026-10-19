using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using B=GloomhavenVR.Core.SteamLink.SteamLinkCoverageBridge;

internal static class Program
{
    private static int checks;
    [ThreadStatic] private static bool inTick;
    private static void Check(bool value,string why) { checks++;if(!value)throw new Exception(why); }
    private static async Task Until(Func<bool> predicate,string why)
    {
        for(int i=0;i<1000&&!predicate();i++)await Task.Delay(2);
        Check(predicate(),why);
    }
    private static byte[] Nonce()=>Enumerable.Range(1,16).Select(i=>(byte)i).ToArray();
    private static byte[] Response(int flags=1)
    {
        var p=B.Request(123,Nonce());p[4]=2;p[10]=(byte)flags;p[20]=45;
        p[40]=77;p[56]=1;return p;
    }
    private static void Wire()
    {
        byte[] request=B.Request(123,Nonce());
        Check(request.Length==128&&request[4]==1&&request[6]==1&&request[8]==128,"fixed request framing");
        Check(Convert.ToHexString(request).ToLowerInvariant()=="53424e310100010080000000010000007b000000000000000102030405060708090a0b0c0d0e0f1000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000","independent SBN1 request golden");
        Check(request[10]==0&&request[20]==0&&request.Skip(40).All(v=>v==0),"request has no claimed server/session/mask authority");
        Check(request[12]==1&&request[16]==123,"request is explicit source profile and actual app process");
        Check(B.Reply(Response(),123,45,Nonce())!.CanAcceptOrdinaryRgba,"prepared full-RGBA capability admitted");
        Check(!B.Reply(Response(),123,45,Nonce())!.CoverageOperational,"negotiated does not mean coverage operational");
        Check(B.Reply(Response(3),123,45,Nonce())!.CoverageOperational,"separate operational bit");
        Check(!B.Reply(Response(0),123,45,Nonce())!.CanAcceptOrdinaryRgba,"known pending receiver never grants source permission");
        foreach(int at in new[]{0,4,6,8,12,16,20,24,39,64,127})
        {
            var mutated=Response();mutated[at]^=0x40;
            Check(B.Reply(mutated,123,45,Nonce())==null,"bad field rejected at "+at);
        }
        foreach(int flags in new[]{2,4,5,128})Check(B.Reply(Response(flags),123,45,Nonce())==null,"unknown or impossible flags rejected");
        var emptyBinding=Response();Array.Clear(emptyBinding,40,16);
        Check(B.Reply(emptyBinding,123,45,Nonce())==null,"accepted source requires actual session binding");
        var noEncoder=Response();Array.Clear(noEncoder,56,8);
        Check(B.Reply(noEncoder,123,45,Nonce())==null,"accepted source requires current encoder generation");
        Check(B.Reply(Response(),124,45,Nonce())==null,"app PID mismatch");
        Check(B.Reply(Response(),123,46,Nonce())==null,"server PID mismatch");
        Check(B.Reply(Response(),123,45,new byte[16])==null,"empty nonce cannot admit a source");
        Check(B.Reply(new byte[127],123,45,Nonce())==null,"truncated message rejected");
        Check(B.Reply(new byte[129],123,45,Nonce())==null,"oversized message rejected");
        foreach(var nonce in new[]{new byte[16],new byte[15]})
        {
            try{B.Request(123,nonce);throw new Exception("invalid nonce was accepted");}
            catch(ArgumentException){checks++;}
        }
    }
    private sealed class Observer
    {
        internal readonly TaskCompletionSource<bool> Finish=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<bool> Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Action<B.Snapshot>? Publish;
        internal bool Cancelled;
        internal async Task Run(Action<B.Snapshot> publish,CancellationToken token)
        {
            Publish=publish;using var registration=token.Register(()=>Cancelled=true);
            Started.TrySetResult(true);await Finish.Task;
        }
    }
    private static async Task Lifecycle()
    {
        var first=new Observer();var second=new Observer();int calls=0;
        var monitor=new B.Monitor((publish,token)=>(Interlocked.Increment(ref calls)==1?first:second).Run(publish,token));
        monitor.Tick(false,0);Check(calls==0&&!monitor.Current.CanAcceptOrdinaryRgba,"Off has zero observer activity");
        monitor.Tick(true,0);await first.Started.Task;
        first.Publish!(new B.Snapshot(1,45,true,false));
        Check(monitor.Current.CanAcceptOrdinaryRgba&&!monitor.Current.CoverageOperational,"only current prepared source published");
        for(int i=0;i<50;i++)monitor.Tick(true,i);
        Check(calls==1,"no observer restart or feature expiry in a healthy session");
        monitor.Reset();Check(!monitor.Current.CanAcceptOrdinaryRgba,"Reset revokes before cleanup finishes");
        await Until(()=>first.Cancelled,"Reset requests owned observer cancellation off-thread");
        monitor.Tick(true,60);Check(calls==1,"new epoch cannot overlap an old observer");
        first.Publish!(new B.Snapshot(1,45,true,true));
        Check(!monitor.Current.CanAcceptOrdinaryRgba,"late old observer cannot revive permission");
        first.Finish.TrySetResult(true);await monitor.Settled;
        monitor.Tick(true,60);await second.Started.Task;
        second.Publish!(new B.Snapshot(2,45,true,true));
        Check(monitor.Current.CoverageOperational,"fresh observer can publish its current native generation");
        second.Publish!(B.Snapshot.Unavailable);
        Check(!monitor.Current.CanAcceptOrdinaryRgba,"EOF/rejection can revoke before owned teardown");
        second.Publish!(new B.Snapshot(2,45,true,false));
        monitor.Tick(false,61);Check(!monitor.Current.CanAcceptOrdinaryRgba,"MR Off synchronously revokes");
        await Until(()=>second.Cancelled,"MR Off requests owned observer cancellation off-thread");
        second.Finish.TrySetResult(true);await monitor.Settled;
        Check(!monitor.Current.CanAcceptOrdinaryRgba,"ended worker cannot retain a snapshot");

        int failed=0;var failures=new B.Monitor((_,_)=>{Interlocked.Increment(ref failed);throw new InvalidOperationException();});
        for(int attempt=0;attempt<3;attempt++)
        {
            failures.Tick(true,attempt*2);await Until(()=>failed==attempt+1,"bounded failed observer attempt");await failures.Settled;
            failures.Tick(true,attempt*2+.5);Check(failed==attempt+1,"failed retry floor respected");
        }
        failures.Tick(true,100);Check(failed==3&&!failures.Current.CanAcceptOrdinaryRgba,"missing peer stays inactive after bounded attempts");
        failures.Tick(true,double.NaN);Check(!failures.Current.CanAcceptOrdinaryRgba,"invalid cold clock is ineligible");

        await CancellationFailures();
        await BlockingCancellation();
    }

    private sealed class CancellationObserver
    {
        internal readonly TaskCompletionSource<bool> Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<bool> Finish=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<bool> BodyEnded=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<bool> CancelEntered=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly ManualResetEventSlim ReleaseCancel=new(false);
        internal Action<B.Snapshot>? Publish;
        internal bool ThrowOnCancel,BlockOnCancel,CallbackOnTick;
        internal int CancelCalls;
        internal async Task Run(Action<B.Snapshot> publish,CancellationToken token)
        {
            Publish=publish;
            // Retain a real registered callback after the observer body ends:
            // retirement must also own any callback still executing elsewhere.
            token.Register(()=>
            {
                Interlocked.Increment(ref CancelCalls);CallbackOnTick=inTick;CancelEntered.TrySetResult(true);
                if(BlockOnCancel)ReleaseCancel.Wait();
                if(ThrowOnCancel)throw new ObjectDisposedException("SafePipeHandle","Safe handle has been closed");
            });
            Started.TrySetResult(true);
            await Finish.Task;BodyEnded.TrySetResult(true);
        }
    }

    private static async Task CancellationFailures()
    {
        var old=new CancellationObserver{ThrowOnCancel=true};var fresh=new CancellationObserver{ThrowOnCancel=true};int calls=0;
        var monitor=new B.Monitor((publish,token)=>(Interlocked.Increment(ref calls)==1?old:fresh).Run(publish,token));
        monitor.Tick(true,0);await old.Started.Task;old.Publish!(new B.Snapshot(1,45,true,true));
        Exception? escaped=null;
        inTick=true;try{monitor.Tick(false,1);}catch(Exception error){escaped=error;}finally{inTick=false;}
        Check(escaped==null,"cancellation callback failure cannot escape Tick");
        Check(!monitor.Current.CanAcceptOrdinaryRgba,"throwing cancellation revokes source authority synchronously");
        await old.CancelEntered.Task;
        Check(!old.CallbackOnTick,"cancellation callback does not execute on the XR tick stack");
        monitor.Tick(true,2);Check(calls==1,"throwing cancellation cannot overlap another observer");
        old.Publish!(new B.Snapshot(1,45,true,true));
        Check(!monitor.Current.CanAcceptOrdinaryRgba,"failed old cancellation cannot revive the next epoch");
        old.Finish.TrySetResult(true);await monitor.Settled;
        monitor.Tick(true,2);await fresh.Started.Task;fresh.Publish!(new B.Snapshot(2,45,true,true));
        Check(monitor.Current.CoverageOperational,"fresh epoch survives a failed cancellation callback");
        escaped=null;inTick=true;try{monitor.Reset();}catch(Exception error){escaped=error;}finally{inTick=false;}
        Check(escaped==null,"cancellation callback failure cannot escape Reset");
        Check(!monitor.Current.CanAcceptOrdinaryRgba,"Reset revokes before a throwing callback settles");
        fresh.Finish.TrySetResult(true);await monitor.Settled;
        Check(old.CancelCalls==1&&fresh.CancelCalls==1,"each owned observer is cancelled at most once");
        Check(GloomhavenVR.Core.VRLog.Messages.Any(s=>s.Contains("cancel failure=AggregateException")),
            "callback failure is bounded diagnostic evidence rather than a tick exception");
    }

    private static async Task BlockingCancellation()
    {
        var old=new CancellationObserver{BlockOnCancel=true};var fresh=new Observer();int calls=0;
        var monitor=new B.Monitor((publish,token)=>Interlocked.Increment(ref calls)==1?old.Run(publish,token):fresh.Run(publish,token));
        monitor.Tick(true,0);await old.Started.Task;old.Publish!(new B.Snapshot(1,45,true,true));
        Task stop=Task.Run(()=>{inTick=true;try{monitor.Tick(false,1);}finally{inTick=false;}});
        try
        {
            await old.CancelEntered.Task;
            await Until(()=>stop.IsCompleted,"tick returns while cancellation callback is blocked");await stop;
            Check(!old.CallbackOnTick,"blocking disposal callback is not run on the tick stack");
            Check(!monitor.Current.CanAcceptOrdinaryRgba,"blocked callback cannot retain source permission");
            old.Finish.TrySetResult(true);await old.BodyEnded.Task;
            // BodyEnded signals before the monitor's async finally resumes.
            // Give that actual continuation an opportunity to finish: a bare
            // instantaneous IsCompleted check let the causal mutant race past.
            Task lifetime=monitor.Settled;
            await Task.WhenAny(lifetime,Task.Delay(200));
            Check(!lifetime.IsCompleted,"observer lifetime includes its pending cancellation callbacks");
            monitor.Tick(true,2);monitor.Reset();monitor.Tick(true,3);
            Check(calls==1&&old.CancelCalls==1,"rapid Off Reset On cannot overlap or double-cancel owned teardown");
            old.Publish!(new B.Snapshot(1,45,true,true));
            Check(!monitor.Current.CanAcceptOrdinaryRgba,"late blocked observer cannot grant a new epoch");
        }
        finally
        {
            old.ReleaseCancel.Set();old.Finish.TrySetResult(true);await stop;await monitor.Settled;
        }
        monitor.Tick(true,3);await fresh.Started.Task;fresh.Publish!(new B.Snapshot(3,45,true,false));
        Check(monitor.Current.CanAcceptOrdinaryRgba,"a new observer starts after all old callback work settles");
        monitor.Reset();fresh.Finish.TrySetResult(true);await monitor.Settled;old.ReleaseCancel.Dispose();
    }
    private static void Paths()
    {
        string registered="{\"jsonid\":\"vrpathreg\",\"version\":1,\"runtime\":[\"C:\\\\SteamVR\"]}";
        Check(B.RuntimeServers(registered).SequenceEqual(new[]{@"C:\SteamVR\bin\win64\vrcompositor.exe",@"C:\SteamVR\bin\win64\vrserver.exe"}),"canonical registered Windows server path");
        Check(B.RuntimeServers(registered.Replace("vrpathreg","other")).Length==0,"foreign registry schema refused");
        Check(B.RuntimeServers(registered.Replace("\"version\":1","\"version\":2")).Length==0,"unknown registry version refused");
        Check(B.RuntimeServers(registered.Replace("C:\\\\SteamVR","C:\\\\..\\\\other")).Length==0,"parent traversal runtime refused");
        Check(B.RuntimeServers(registered.Replace("{","{\"runtime\":[],")).Length==0,"duplicate runtime key refused");
        Check(B.RuntimeServers(new string('x',16385)).Length==0,"registry byte/character budget refused");
    }
    public static async Task Main(string[] args)
    {
        if(args.Length==0||args.Contains("--wire"))Wire();
        if(args.Length==0||args.Contains("--paths"))Paths();
        if(args.Length==0||args.Contains("--lifecycle"))await Lifecycle();
        if(args.Contains("--cancel-lifetime"))await BlockingCancellation();
        Console.WriteLine(checks+" coverage bridge assertions passed");
    }
}
