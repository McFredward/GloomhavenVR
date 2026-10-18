using System;
using System.Reflection;
using GloomhavenVR.Core;
using GloomhavenVR.Core.SteamLink;
using UnityEngine;
internal static class Program
{
    private static int _checks;
    private static void Check(bool value,string why) { _checks++;if(!value)throw new Exception(why); }
    private static SceneHeadOutput Current => (SceneHeadOutput)typeof(SteamLinkSceneRuntime).GetField("_output",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
    private static void Render(SceneHeadOutput output,RenderTexture input)
    { try { typeof(SceneHeadOutput).GetMethod("OnRenderImage",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(output,new object[]{input,new RenderTexture()}); }catch(TargetInvocationException ex){throw ex.InnerException!;} }
    private static Camera Start()
    {
        SteamLinkSceneRuntime.Reset();SceneEyeOutput.Inputs.Clear();SceneEyeOutput.Packed=0;SceneEyeOutput.Next=SceneEyeResult.Rendered;SceneEyeOutput.RefuseZero=SceneEyeOutput.ThrowZero=false;SteamLinkProjectionProbe.Failures=0;
        SteamLinkStreamQuality.Prepared=true;VRSession.IsRunning=true;Time.frameCount+=10;
        var head=new Camera();VRCameraPolicy.AllowedHead=head;Check(SteamLinkSceneRuntime.TryEnter(head),"normal source prepared");
        Check(!SteamLinkSceneRuntime.ProjectionProbeSourceReady,"no explicit complete stereo cohort cannot arm probe");
        Render(Current,new RenderTexture());head.stereoActiveEye=Camera.MonoOrStereoscopicEye.Right;Render(Current,new RenderTexture());Time.frameCount++;
        Render(Current,new RenderTexture());Check(SteamLinkSceneRuntime.ProjectionProbeSourceReady,"only robust v3 admitted current source qualifies");
        SceneEyeOutput.Inputs.Clear();return head;
    }
    public static void Main()
    {
        foreach(string mode in new[]{"normal","zero","refuse","throw"})
        {
            var head=Start();var source=new RenderTexture();SteamLinkProjectionProbe.Zero=new RenderTexture{width=1,height=1,antiAliasing=1};SteamLinkProjectionProbe.UseZero=mode!="normal";
            SceneEyeOutput.RefuseZero=mode=="refuse";SceneEyeOutput.ThrowZero=mode=="throw";Render(Current,source);
            Check(SteamLinkSceneRuntime.Active && Current.Failure=="","diagnostic cannot poison production MR: "+mode);
            Check(SteamLinkSceneRuntime.TryEnter(head),"actual head never latched by diagnostic: "+mode);
            int expected=mode=="refuse"||mode=="throw"?2:1;Check(SceneEyeOutput.Inputs.Count==expected,"exact expected probe/normal attempts: "+mode);
            if(mode=="zero")Check(ReferenceEquals(SceneEyeOutput.Inputs[0],SteamLinkProjectionProbe.Zero),"owned zero only passed to existing packer");
            else Check(ReferenceEquals(SceneEyeOutput.Inputs[expected-1],source),"original untouched source used for normal/fallback: "+mode);
            Check(SteamLinkProjectionProbe.Failures==(expected==2?1:0),"only isolated test attempt stopped: "+mode);
        }
        Start();SteamLinkProjectionProbe.UseZero=true;var wrongWidth=new RenderTexture{width=7000};Render(Current,wrongWidth);
        Check(SceneEyeOutput.Inputs.Count==0,"HostPrepared uses actual source extent before zero substitution");
        Start();SteamLinkStreamQuality.Prepared=false;Check(!SteamLinkSceneRuntime.ProjectionProbeSourceReady,"actual host readiness loss invalidates probe admission");
        Start();VRCameraPolicy.AllowedHead=new Camera();Check(!SteamLinkSceneRuntime.ProjectionProbeSourceReady,"actual head authority loss invalidates probe admission");
        Start();VRSession.IsRunning=false;Check(!SteamLinkSceneRuntime.ProjectionProbeSourceReady,"actual session loss invalidates probe admission");
        SteamLinkSceneRuntime.Reset();Console.WriteLine("projection actual callback ports: "+_checks+" assertions; actual borrowed inputs preserved, GPU external");
    }
}
