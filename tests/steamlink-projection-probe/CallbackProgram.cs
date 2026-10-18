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
        SteamLinkSceneRuntime.Reset();SceneEyeOutput.Inputs.Clear();SceneEyeOutput.Packed=0;SceneEyeOutput.Next=SceneEyeResult.Rendered;SteamLinkProjectionProbe.ApiReady=false;
        SteamLinkStreamQuality.Prepared=true;VRSession.IsRunning=true;Time.frameCount+=10;
        var head=new Camera();VRCameraPolicy.AllowedHead=head;Check(SteamLinkSceneRuntime.TryEnter(head),"normal source prepared");
        Check(!SteamLinkSceneRuntime.ProjectionProbeSourceReady,"no explicit complete stereo cohort cannot arm probe");
        Render(Current,new RenderTexture());head.stereoActiveEye=Camera.MonoOrStereoscopicEye.Right;Render(Current,new RenderTexture());Time.frameCount++;
        Render(Current,new RenderTexture());Check(SteamLinkSceneRuntime.ProjectionProbeSourceReady,"only robust v3 admitted current source qualifies");
        SceneEyeOutput.Inputs.Clear();return head;
    }
    public static void Main()
    {
        foreach(string mode in new[]{"ordinary","starting","ready","refused"})
        {
            var head=Start();SteamLinkProjectionProbe.ApiReady=mode=="ready";
            foreach(var eye in new[]{Camera.MonoOrStereoscopicEye.Left,Camera.MonoOrStereoscopicEye.Right})
            {
                head.stereoActiveEye=eye;var source=new RenderTexture();Render(Current,source);
                Check(SceneEyeOutput.Inputs.Count==1 && ReferenceEquals(SceneEyeOutput.Inputs[0],source),"READY never substitutes the actual scene source: "+mode+"/"+eye);
                Check(SteamLinkSceneRuntime.Active && Current.Failure=="","preview cannot poison production MR: "+mode);
                SceneEyeOutput.Inputs.Clear();
            }
        }
        Start();SteamLinkProjectionProbe.ApiReady=true;var wrongWidth=new RenderTexture{width=7000};Render(Current,wrongWidth);
        Check(SceneEyeOutput.Inputs.Count==0,"HostPrepared still validates the actual source extent");
        Start();SteamLinkStreamQuality.Prepared=false;Check(!SteamLinkSceneRuntime.ProjectionProbeSourceReady,"actual host readiness loss invalidates probe admission");
        Start();VRCameraPolicy.AllowedHead=new Camera();Check(!SteamLinkSceneRuntime.ProjectionProbeSourceReady,"actual head authority loss invalidates probe admission");
        Start();VRSession.IsRunning=false;Check(!SteamLinkSceneRuntime.ProjectionProbeSourceReady,"actual session loss invalidates probe admission");
        SteamLinkSceneRuntime.Reset();Console.WriteLine("projection actual callback ports: "+_checks+" assertions; actual borrowed inputs preserved, GPU external");
    }
}
