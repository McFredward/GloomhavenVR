using System;
using System.IO;
using System.Threading.Channels;
using System.Threading.Tasks;
using GloomhavenVR.Core;
using GloomhavenVR.Core.SteamLink;
using GloomhavenVR.WorldUI;
using UnityEngine;
using Probe=GloomhavenVR.Core.SteamLink.SteamLinkProjectionProbe;
internal static class Program
{
    private static int _checks;
    private static void Check(bool yes,string message) { _checks++;if(!yes)throw new Exception(message); }
    private static async Task Until(Func<bool> yes) { for(int n=0;n<400;n++) { if(yes())return;await Task.Delay(5); } throw new Exception("Timed out waiting for owned worker"); }
    private sealed class Pipe : TextReader
    {
        private readonly Channel<char> _channel=Channel.CreateUnbounded<char>();
        internal void Put(string value) { foreach(char c in value) if(!_channel.Writer.TryWrite(c))return; }
        internal void Finish() => _channel.Writer.TryComplete();
        public override async Task<int> ReadAsync(char[] buffer,int start,int count)
        {
            if(!await _channel.Reader.WaitToReadAsync())return 0;
            int n=0;while(n<count && _channel.Reader.TryRead(out char c))buffer[start+n++]=c;return n;
        }
    }
    private sealed class Child : SteamLinkCompositorMonitor.IChild
    {
        internal readonly Pipe Pipe=new();
        private readonly TaskCompletionSource<bool> _exit=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<bool>? DelayedStop;
        internal int Stops;internal bool StopResult=true;
        public TextReader Output=>Pipe;
        public Task Exited=>_exit.Task;
        internal void Exit() { Pipe.Finish();_exit.TrySetResult(true); }
        public async Task<bool> StopAsync() { Stops++;Exit();if(DelayedStop!=null)await DelayedStop.Task;return StopResult; }
    }
    private static async Task Ready(Probe.Owner owner,Child child)
    { Check(owner.Arm(true,true),"explicit action arms");owner.Tick(true,false);await Until(()=>owner.Current==Probe.Phase.Starting);child.Pipe.Put(Probe.ReadyLine+"\n");await Until(()=>owner.Current==Probe.Phase.Active); }
    private static async Task Main()
    {
        Check(Probe.Eligible,"baseline full eligibility");
        VRLog.WantsDebug=false;Check(!Probe.Eligible,"Debug logging is mandatory");VRLog.WantsDebug=true;
        VRSession.IsRunning=false;Check(!Probe.Eligible,"session is mandatory");VRSession.IsRunning=true;
        VROptionsTab.SetCheats(false);Check(!Probe.Eligible,"Cheats are mandatory");VROptionsTab.SetCheats(true);
        MixedReality.Enabled.Value=false;Check(!Probe.Eligible,"MR is mandatory");MixedReality.Enabled.Value=true;
        SteamLinkSceneRuntime.Requested=false;Check(!Probe.Eligible,"native Frame Link profile is mandatory");SteamLinkSceneRuntime.Requested=true;
        SteamLinkSceneRuntime.ProjectionProbeSourceReady=false;Check(!Probe.Eligible,"actual admitted robust v3 source is mandatory");SteamLinkSceneRuntime.ProjectionProbeSourceReady=true;
        int launches=0;var child=new Child();var owner=new Probe.Owner(()=>{launches++;return Task.FromResult<SteamLinkCompositorMonitor.IChild>(child);},_=>{});
        Check(!owner.Arm(false,true) && !owner.Arm(true,false),"stale ineligible or closed-menu action refused");
        Check(owner.Arm(true,true),"action pending");for(int n=0;n<5;n++)owner.Tick(true,true);
        Check(launches==0 && owner.Current==Probe.Phase.PendingClose,"initiating menu remains pending until first close");
        Check(!owner.Arm(true,true),"pending duplicate action refused");owner.Tick(true,false);await Until(()=>launches==1);
        child.Pipe.Put("SDK metadata\nX"+Probe.ReadyLine+"\n");await Task.Delay(20);
        Check(owner.Current==Probe.Phase.Starting,"only exact complete double-publish ready is accepted");
        child.Pipe.Put(Probe.ReadyLine+"\r\n");await Until(()=>owner.Current==Probe.Phase.Active);
        Check(owner.OwnsTest,"ready worker owns explicit test");owner.Tick(true,true);await owner.Settled;
        Check(child.Stops==1 && !owner.OwnsTest && owner.Current==Probe.Phase.Idle,"reopening menu immediately stops exactly once");
        for(int n=0;n<6;n++)owner.Tick(true,false);Check(launches==1,"test never restarts by Tick");
        foreach(string ending in new[]{"eof","exit","stopped","bad"})
        {
            var c=new Child{DelayedStop=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)};
            var o=new Probe.Owner(()=>Task.FromResult<SteamLinkCompositorMonitor.IChild>(c),_=>{});await Ready(o,c);
            if(ending=="eof")c.Pipe.Finish();else if(ending=="exit")c.Exit();else c.Pipe.Put(ending=="stopped"?Probe.StoppedLine+"\n":"\0\n");
            await Until(()=>c.Stops==1);Check(o.Current!=Probe.Phase.Active && !o.OwnsTest,"EOF/exit/stopped/failure revokes zero before delayed cleanup: "+ending);
            Check(!o.Arm(true,true),"no new helper while teardown pending");c.DelayedStop.TrySetResult(true);await o.Settled;
        }
        foreach(string bad in new[]{new string('x',Probe.MaximumLine)+"\n",new string('x',1000)+"\n"+new string('x',1000)+"\n"+string.Concat(System.Linq.Enumerable.Repeat(new string('x',1000)+"\n",7)),"\u0080\n","embedded\rreturn\n",Probe.ReadyLine+"\n"+Probe.ReadyLine+"\n"})
        {
            var c=new Child();var o=new Probe.Owner(()=>Task.FromResult<SteamLinkCompositorMonitor.IChild>(c),_=>{});
            Check(o.Arm(true,true),"bounded-output test arms");o.Tick(true,false);await Until(()=>o.Current==Probe.Phase.Starting);c.Pipe.Put(bad);await o.Settled;
            Check(c.Stops==1 && o.Current==Probe.Phase.Failed,"untrusted output refused and child retired");
        }
        var metadataChild=new Child();var metadataLog=new System.Collections.Generic.List<string>();
        var metadataOwner=new Probe.Owner(()=>Task.FromResult<SteamLinkCompositorMonitor.IChild>(metadataChild),text=>metadataLog.Add(text));
        Check(metadataOwner.Arm(true,true),"metadata owner arms");metadataOwner.Tick(true,false);
        metadataChild.Pipe.Put("SFNP_PROJECTION_META_V2 pattern=2048\r\nSFNP_PROJECTION_GEOMETRY_V2 eye=0 full=1,1,1,1\r\nSFNP_PROJECTION_REFUSED_V2 stage=adapter code=1\r\n");await metadataOwner.Settled;
        Check(metadataLog.Exists(s=>s.StartsWith("SFNP_PROJECTION_META_V2 ")) && metadataLog.Exists(s=>s.StartsWith("SFNP_PROJECTION_GEOMETRY_V2 ")) && metadataLog.Exists(s=>s.StartsWith("SFNP_PROJECTION_REFUSED_V2 ")),"known bounded native witness/refusal metadata preserved");
        Check(metadataOwner.Current==Probe.Phase.Failed && metadataChild.Stops==1,"native refusal retires diagnostic without ready");
        var revokedChild=new Child();var revoked=new Probe.Owner(()=>Task.FromResult<SteamLinkCompositorMonitor.IChild>(revokedChild),_=>{});await Ready(revoked,revokedChild);revoked.Tick(false,false);await revoked.Settled;
        Check(!revoked.OwnsTest && revokedChild.Stops==1,"live Debug/Cheats/MR/source permission loss retires helper");
        int failures=0;var failure=new Probe.Owner(()=>{failures++;throw new IOException();},_=>{});
        Check(failure.Arm(true,true),"failed launch arms");failure.Tick(true,false);await failure.Settled;
        for(int n=0;n<10;n++)failure.Tick(true,false);Check(failures==1,"launch failure never auto-retries");
        Check(failure.Arm(true,true),"explicit retry possible after confirmed failed launch");failure.Tick(true,false);await failure.Settled;
        var late=new TaskCompletionSource<SteamLinkCompositorMonitor.IChild>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stale=new Probe.Owner(()=>late.Task,_=>{});Check(stale.Arm(true,true),"late launch arms");stale.Tick(true,false);stale.Stop();
        Check(!stale.Arm(true,true),"stale launch cannot overlap new generation");var lateChild=new Child();late.SetResult(lateChild);await stale.Settled;
        Check(lateChild.Stops==1 && stale.Current==Probe.Phase.Idle,"late launched child gets cooperative EOF");
        var unknown=new Child{StopResult=false};var unsafeOwner=new Probe.Owner(()=>Task.FromResult<SteamLinkCompositorMonitor.IChild>(unknown),_=>{});await Ready(unsafeOwner,unknown);unsafeOwner.Stop();await unsafeOwner.Settled;
        Check(unsafeOwner.Current==Probe.Phase.Unsafe && !unsafeOwner.Arm(true,true),"unknown child ownership is sticky across retry/reset");
        var unsafeLaunch=new Probe.Owner(()=>throw new SteamLinkCompositorMonitor.OwnershipUnknownException(),_=>{});Check(unsafeLaunch.Arm(true,true),"unknown launch arms");unsafeLaunch.Tick(true,false);await unsafeLaunch.Settled;
        Check(unsafeLaunch.Current==Probe.Phase.Unsafe && !unsafeLaunch.Arm(true,true),"unknown post-start construction cannot overlap");
        var original=new RenderTexture(default);var zero=new RenderTexture(default);var frames=new Probe.FrameLatch();
        Check(ReferenceEquals(frames.Select(original,zero,10,false),original),"unready left keeps actual source");
        Check(ReferenceEquals(frames.Select(original,zero,10,true),original),"midpair readiness cannot zero second eye");
        Check(ReferenceEquals(frames.Select(original,zero,11,true),zero),"ready next frame uses owned zero");
        Check(ReferenceEquals(frames.Select(original,zero,11,false),zero),"same pair remains consistent when stop arrives");
        Check(ReferenceEquals(frames.Select(original,zero,12,false),original),"next frame restores immediately");
        zero.Created=false;Check(ReferenceEquals(frames.Select(original,zero,13,true),original),"uncreated zero never selected");zero.Created=true;
        frames.Select(original,zero,13,true);zero.Created=false;Check(ReferenceEquals(frames.Select(original,zero,13,true),original),"midpair zero invalidation keeps borrowed source intact");zero.Created=true;
        frames.Select(original,zero,14,true);frames.Reject();Check(ReferenceEquals(frames.Select(original,zero,14,true),original),"failed test pack retires remaining eye input");
        var old=new RenderTexture(default);RenderTexture.active=old;GL.sRGBWrite=true;Time.frameCount=20;
        Check(VROptionsTab.BuildTest()==1 && VROptionsTab.TestAction!=null,"only Cheats page has test action");VROptionsTab.TestAction!();
        Check(Probe.Status==Probe.Phase.PendingClose && GL.Clears==1 && GL.sRGBWrite && ReferenceEquals(RenderTexture.active,old),"cold zero allocation restores Unity active target and sRGB state");
        VROptionsTab.SetCheats(false);VROptionsTab.TestAction!();Check(Probe.Status==Probe.Phase.Idle,"stale UI action rechecks Cheats");
        Time.frameCount++;Probe.Tick();Check(UnityEngine.Object.Destroyed==1,"owned zero retired without touching original");VROptionsTab.SetCheats(true);
        Check(Loc.SteamLinkProjectionProbeHint.Contains("keep your head still"),"English test explains frozen pose");Loc.CurrentLanguage="German";
        Check(Loc.SteamLinkProjectionProbeHint.Contains("Kopf ruhig"),"German test explains frozen pose");
        Console.WriteLine("projection owner/lifecycle: "+_checks+" assertions; no helper/OpenVR/device execution");
    }
}
