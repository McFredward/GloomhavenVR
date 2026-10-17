using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GloomhavenVR.Core.SteamLink;
using C = GloomhavenVR.Core.SteamLink.SteamLinkConnection;
namespace UnityEngine { public static class JsonUtility { public static T FromJson<T>(string s) => JsonSerializer.Deserialize<T>(s,new JsonSerializerOptions { IncludeFields=true })!; } }
namespace GloomhavenVR.Core { internal static class VRLog { internal static bool WantsDebug => true; internal static int Infos,Warnings; internal static void Debug(string a,string b) { } internal static void Info(string a,string b) { Infos++; } internal static void Warn(string a,string b) { Warnings++; } } }
internal static class Program
{
    private static int checks;
    private static void Check(bool value,string why) { checks++;if(!value)throw new Exception(why); }
    private static string Message(string model="Steam Frame",string driver="vrlink",string registered="vrlink/frame") => JsonSerializer.Serialize(new { schema="ghvr-steamlink-connection-v1",connected=true,device_class=1,tracking_system=driver,model,registered_type=registered });
    private static void Main(string[] args)
    {
        Check(C.Classify(true,1,"vrlink","Steam Frame","vrlink/frame").Available,"live Frame Link");
        Check(C.Classify(true,1,"VRLINK","steam frame","").Available,"exact ASCII insensitive driver/model; optional registered");
        Check(C.Classify(true,1,"","Steam Frame","vrlink/hmd").Available,"registered public driver identity alternative");
        foreach(string model in new[]{"Quest 3","Valve Index","SteamVR HMD","Steam Frame prototype","SteamFrame","my Steam Frame",""})
            Check(!C.Classify(true,1,"vrlink",model,"vrlink/hmd").Available,"other/generic model: "+model);
        foreach(string driver in new[]{"virtualdesktop","oculus","steamvr","vrlink-extra","vrlink ",""})
            Check(!C.Classify(true,1,driver,"Steam Frame","other/vrlink").Available,"wrong backend: "+driver);
        foreach(string registered in new[]{"virtualdesktop/frame","notvrlink/frame","vrlink-other/frame","other/vrlink",""})
            Check(!C.Classify(true,1,"","Steam Frame",registered).Available,"registered falsepositive");
        Check(!C.Classify(false,1,"vrlink","Steam Frame","").Available,"disconnected HMD");
        Check(!C.Classify(true,2,"vrlink","Steam Frame","").Available,"controller never HMD");
        Check(!C.Classify(true,1,"vrlink","Steam Frame\0","").Available,"NUL refused");
        Check(!C.Classify(true,1,new string('x',97),"Steam Frame","").Available,"bounded metadata");
        Check(!C.Classify(true,1,"vrlink","Steam Frame","").ReceiverVerified,"not receiver proof");
        Check(C.Decode(Message(),0).Available,"actual protocol decoding");
        foreach(string bad in new[]{Message("Quest 3"),Message(driver:"virtualdesktop",registered:"virtualdesktop/frame"),Message().Replace("true","false"),Message().Replace("\"device_class\":1","\"device_class\":2"),Message().Replace("\"schema\":","\"extra\":1,\"schema\":"),Message().Replace("\"connected\":true","\"connected\":true,\"connected\":false"),"{}","null","garbage",new string('x',2049)})
            Check(!C.Decode(bad,0).Available,"invalid protocol retained unavailable");
        Check(C.Decode("{\"schema\":\"ghvr-steamlink-connection-v1\",\"error\":\"changed\"}",2).State==C.Reason.Changed,"changed stable identity refused");
        foreach(string path in new[]{@"C:\SteamVR\bin\win64\openvr_api.dll",@"D:\Games\Steam VR\api.dll"})Check(C.LocalWindowsPath(path),"local path accepted");
        foreach(string path in new[]{@"\\server\share\api.dll",@"C:relative.dll",@"C:/network",@"C:\..\api.dll",@"C:\SteamVR\..\api.dll","",@"C:\x"+'\n',@"C:\x:y"})Check(!C.LocalWindowsPath(path),"foreign/network/relative path refused");
        byte[] exe=File.ReadAllBytes(args[0]);Check(C.HelperPin(exe),"actual committed helper pin+PE64");
        for(int i=0;i<8;i++){byte[] changed=(byte[])exe.Clone();changed[i==0?0:i==1?60:changed.Length-i]^=1;Check(!C.HelperPin(changed),"mutated helper refused");}
        Check(!C.Pe64(exe,true),"EXE not DLL");Check(!C.HelperPin(exe.Take(exe.Length-1).ToArray()),"truncated resource refused");
        byte[] api=File.ReadAllBytes(args[1]);
        Check(C.ArtifactPin(api,C.ApiBytes,C.ApiSha256,true),"official SDK loader pin and PE64 DLL");
        Check(!C.ArtifactPin(api,C.ApiBytes,C.ApiSha256,false),"SDK DLL cannot replace reader executable");
        Check(!C.ArtifactPin(exe,C.HelperBytes,C.HelperSha256,true),"reader executable cannot replace SDK DLL");
        Check(C.ReadResource(C.ApiResource,C.ApiBytes,C.ApiSha256,true)!.SequenceEqual(api),"actual SDK embedded resource through production reader");
        Check(C.ReadResource(C.Resource,C.HelperBytes,C.HelperSha256,false)!.SequenceEqual(exe),"actual helper embedded resource through production reader");
        Check(C.ReadResource(C.ApiResource,C.ApiBytes,C.ApiSha256,false)==null,"embedded wrong-kind SDK refused");
        Check(C.ReadResource(C.ApiResource,C.ApiBytes-1,C.ApiSha256,true)==null,"truncated embedded length refused");
        Check(C.ReadResource(C.ApiResource,C.ApiBytes,new string('0',64),true)==null,"changed embedded SDK pin refused");
        Check(C.ReadResource(C.ApiResource+".missing",C.ApiBytes,C.ApiSha256,true)==null,"missing SDK cannot silently use installed runtime copy");
        byte[] altered=(byte[])api.Clone();altered[altered.Length-1]^=1;
        Check(!C.ArtifactPin(altered,C.ApiBytes,C.ApiSha256,true),"changed SDK bytes refused");
        Check(C.Decode("{\"schema\":\"ghvr-steamlink-connection-v1\",\"error\":\"initialization\"}",2).Detail=="reader:initialization","actual helper failure stage retained");
        Check(C.Decode("{\"schema\":\"ghvr-steamlink-connection-v1\",\"error\":\"untrusted\\ntext\"}",2).Detail=="reader:invalid-error","untrusted child failure text not logged");
        TestLogging();
        TestMonitor();
        TestChild();
        Console.WriteLine("{\"managed_connection_checks\":"+checks+",\"hardware_identity_verified\":false}");
    }
    private static void TestLogging()
    {
        var pending=new C.Snapshot(C.Reason.Pending);var ready=C.Classify(true,1,"vrlink","Steam Frame","");
        C.Reset();C.LogTransition(pending,ready);C.LogTransition(ready,ready);
        Check(GloomhavenVR.Core.VRLog.Infos==1,"one useful positive Info transition, no repeated info");
        foreach(var reason in new[]{C.Reason.RuntimePath,C.Reason.HelperUnavailable})
            for(int i=0;i<20;i++)C.LogTransition(pending,new C.Snapshot(reason));
        Check(GloomhavenVR.Core.VRLog.Warnings==2,"warning once per reason and session");
        foreach(var reason in new[]{C.Reason.WrongHeadset,C.Reason.WrongTransport,C.Reason.NativeUnavailable})
            C.LogTransition(pending,new C.Snapshot(reason));
        Check(GloomhavenVR.Core.VRLog.Warnings==2,"ordinary unsupported connection is not warning spam");
        for(int i=0;i<20;i++)C.LogTransition(pending,new C.Snapshot(C.Reason.NativeUnavailable,detail:"reader:initialization"));
        Check(GloomhavenVR.Core.VRLog.Warnings==3,"bounded actionable child failure warning once per session");
        C.Reset();C.LogTransition(pending,new C.Snapshot(C.Reason.RuntimePath));
        Check(GloomhavenVR.Core.VRLog.Warnings==4,"new session retains useful failure warning");
        C.Reset();
    }
    private static void TestChild()
    {
        if (OperatingSystem.IsWindows()) return; // Portable fake process; not a Windows/API identity claim.
        string directory=Path.Combine(Path.GetTempPath(),"steamlink-child-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        string helper=Path.Combine(directory,"owned-reader"), marker=Path.Combine(directory,"child.pid");
        void Script(string text) { File.WriteAllText(helper,"#!/bin/sh\n"+text+"\n");File.SetUnixFileMode(helper,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute); }
        try
        {
            Script("printf '%s\\n' '"+Message()+"'");
            Check(C.RunChild(helper,marker,CancellationToken.None).Available,"real managed launch/pipe/parser path with explicit fake metadata");
            string[] names={"VR_OVERRIDE","VR_PATHREG_OVERRIDE","VR_CONFIG_PATH","VR_LOG_PATH","LD_PRELOAD","LD_AUDIT"};
            string?[] saved=names.Select(Environment.GetEnvironmentVariable).ToArray();
            try
            {
                foreach(string name in names)Environment.SetEnvironmentVariable(name,"owned-fixture");
                Script("test \"$VR_OVERRIDE\" = owned-fixture && test \"$VR_PATHREG_OVERRIDE\" = owned-fixture && test \"$VR_CONFIG_PATH\" = owned-fixture && test \"$VR_LOG_PATH\" = owned-fixture && test -z \"$LD_PRELOAD\" && test -z \"$LD_AUDIT\" || exit 4; printf '%s\\n' '"+Message()+"'");
                Check(C.RunChild(helper,marker,CancellationToken.None).Available,"owned child preserves native OpenVR selection overrides, strips unrelated injection");
            }
            finally { for(int i=0;i<names.Length;i++)Environment.SetEnvironmentVariable(names[i],saved[i]); }
            Script("printf '%s\\n' 'not metadata'");Check(!C.RunChild(helper,marker,CancellationToken.None).Available,"foreign output refused");
            Script("printf '%s\\n' 'warning' >&2");Check(!C.RunChild(helper,marker,CancellationToken.None).Available,"stderr cannot masquerade as valid metadata");
            Script("while :; do printf '%s' '0123456789012345678901234567890123456789012345678901234567890123456789'; done");
            Check(!C.RunChild(helper,marker,CancellationToken.None).Available,"oversized stdout kills only owned reader");
            Script("printf '%s' $$ > \"$1\"; while :; do :; done");
            using(var cancelled=new CancellationTokenSource())
            {
                cancelled.Cancel();Check(!C.RunChild(helper,marker,cancelled.Token).Available&&!File.Exists(marker),"cancel-before-start never spawns");
            }
            using(var cancel=new CancellationTokenSource())
            {
                Task<C.Snapshot> child=Task.Run(()=>C.RunChild(helper,marker,cancel.Token));
                Check(SpinWait.SpinUntil(()=>File.Exists(marker),2000),"actual owned test child started");
                int pid=int.Parse(File.ReadAllText(marker));cancel.Cancel();Check(child.Wait(2000)&&!child.Result.Available,"cancel asynchronous owned child");
                bool gone;try{using var p=Process.GetProcessById(pid);gone=p.HasExited;}catch(ArgumentException){gone=true;}
                Check(gone,"cancelled own child reaped");File.Delete(marker);
            }
            var clock=Stopwatch.StartNew();Check(!C.RunChild(helper,marker,CancellationToken.None).Available,"actual bounded deadline");
            Check(clock.ElapsedMilliseconds>=C.HelperTimeoutMs-100&&clock.ElapsedMilliseconds<8000,"five-second deadline plus bounded reaping");
            int timeoutPid=int.Parse(File.ReadAllText(marker));bool reaped;try{using var p=Process.GetProcessById(timeoutPid);reaped=p.HasExited;}catch(ArgumentException){reaped=true;}
            Check(reaped,"deadline own child reaped");
        }
        finally { Directory.Delete(directory,true); }
    }
    private static void TestMonitor()
    {
        int calls=0;var result=new TaskCompletionSource<C.Snapshot>();CancellationToken token=default;
        var m=new C.Monitor(t=>{calls++;token=t;return result.Task;});
        m.Tick(false,0);Check(calls==0&&!m.Current.Available,"no active SteamVR session no query");
        m.Tick(true,0);Check(calls==1&&!m.Current.Available,"first cold query pending");
        for(int i=0;i<200;i++)m.Tick(true,i*.001);Check(calls==1,"one pending reader; no tick spawns");
        m.Invalidate();Check(token.IsCancellationRequested&&!m.Current.Available,"device change cancels current only");
        result.SetResult(C.Classify(true,1,"vrlink","Steam Frame",""));m.Tick(true,1);
        Check(!m.Current.Available&&calls==1,"old session answer never adopted");
        result=new TaskCompletionSource<C.Snapshot>();m.Tick(true,2);Check(calls==2,"device-change query respects rate limit");
        result.SetResult(C.Classify(true,1,"vrlink","Steam Frame",""));m.Tick(true,2.1);Check(m.Current.Available,"current successful identity publishes");
        for(int i=3;i<500;i++)m.Tick(true,i);Check(calls==2,"stable snapshot has no polling");
        m.Reset();Check(!m.Current.Available,"reset clears effective available");
        int misses=0;var failed=new C.Monitor(t=>{misses++;return Task.FromResult(new C.Snapshot(C.Reason.NativeUnavailable));});
        for(int i=0;i<200;i++)failed.Tick(true,i);Check(misses==C.Monitor.MaximumAttempts,"bounded startup retries");
        failed.Tick(false,300);failed.Tick(true,301);Check(misses==C.Monitor.MaximumAttempts+1,"new active session starts fresh bounded attempt");
        failed.Tick(true,double.NaN);Check(!failed.Current.Available,"invalid clock fails closed");
        Check(!C.Available,"public property pure cached starts unavailable");
    }
}
