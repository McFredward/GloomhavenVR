using System;
using System.Reflection;
using GloomhavenVR.Core;
using GloomhavenVR.Core.SteamLink;
using UnityEngine;

internal static class Program
{
    private static int checks;
    private static readonly FieldInfo CurrentField=typeof(SteamLinkSceneRuntime).GetField("_output",BindingFlags.Static|BindingFlags.NonPublic)!;
    private static SceneHeadOutput? Current=>(SceneHeadOutput?)CurrentField.GetValue(null);
    private static void Check(bool condition,string why)
    { checks++;if(!condition)throw new InvalidOperationException(why); }
    private static void Invoke(SceneHeadOutput output,string name,params object[] args)
    {
        try{typeof(SceneHeadOutput).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(output,args);}
        catch(TargetInvocationException ex){throw ex.InnerException!;}
    }
    private static Camera Fresh()
    {
        SteamLinkSceneRuntime.Reset();VRLog.Warnings.Clear();SteamLinkCoverageBridge.CanAcceptOrdinaryRgba=true;
        SceneEyeOutput.Next=SceneEyeResult.Rendered;SceneEyeOutput.Ordinary=0;Graphics.Ordinary=0;
        MixedReality.Enabled.Value=true;MixedReality.SteamLinkNative.Value=true;
        SteamLinkConnection.Available=true;VRSession.IsRunning=true;VRSession.InputFocus=true;
        UnityEngine.XR.XRSettings.eyeTextureWidth=UnityEngine.XR.XRSettings.eyeTextureHeight=4040;
        QualitySettings.activeColorSpace=ColorSpace.Gamma;
        Time.frameCount=100;
        var head=new Camera();VRCameraPolicy.AllowedHead=head;return head;
    }
    private static SceneHeadOutput Enter(Camera head,bool prime=true)
    {
        Check(SteamLinkSceneRuntime.TryEnter(head),"ready stereo head enters");Check(Current!=null&&Current.Ready,"one ready source");
        var output=Current!;
        if(prime)
        {
            var saved=head.stereoActiveEye;
            head.stereoActiveEye=Camera.MonoOrStereoscopicEye.Left;Render(output,head);
            head.stereoActiveEye=Camera.MonoOrStereoscopicEye.Right;Render(output,head);
            Check(SceneEyeOutput.Ordinary==0,"first complete startup cohort must stay ordinary in both eyes");
            Time.frameCount++;head.stereoActiveEye=saved;Graphics.Ordinary=0;
        }
        return output;
    }
    private static void Image(SceneHeadOutput output)
        =>Invoke(output,"OnRenderImage",new RenderTexture(),new RenderTexture());
    private static void Render(SceneHeadOutput output,Camera head)
    { Invoke(output,"OnPreCull");Camera.Begin(head);Image(output);Camera.End(head); }
    private static SceneHeadOutput Prime(Camera head)
    {
        Check(SteamLinkSceneRuntime.TryEnter(head),"fresh full-RGB source can prepare");
        var output=Current!;
        head.stereoActiveEye=Camera.MonoOrStereoscopicEye.Left;Render(output,head);
        head.stereoActiveEye=Camera.MonoOrStereoscopicEye.Right;Render(output,head);
        Time.frameCount++;head.stereoActiveEye=Camera.MonoOrStereoscopicEye.Left;
        return output;
    }
    private static void CapabilityAdmission()
    {
        var head=Fresh();SteamLinkCoverageBridge.CanAcceptOrdinaryRgba=false;
        Check(!SteamLinkSceneRuntime.TryEnter(head),"Pending paired capability must not prepare a source");
        Check(head.gameObject.Components==0&&!SteamLinkSceneRuntime.Active,"Pending capability creates no resources");
        Check(SteamLinkSceneRuntime.Requested,"Pending capability retains native request without chroma fallback");
        SteamLinkCoverageBridge.CanAcceptOrdinaryRgba=true;var output=Enter(head);Render(output,head);
        Check(SceneEyeOutput.Ordinary==1,"Prepared capability preserves full RGB");
        foreach(bool late in new[]{false,true})
        {
            head=Fresh();output=Enter(head);
            if(late)Invoke(output,"OnPreCull");
            SteamLinkCoverageBridge.CanAcceptOrdinaryRgba=false;
            if(!late)
            {
                Invoke(output,"OnPreCull");
                Check(!output.Ready,"Capability loss retires the current stereo epoch");
            }
            Image(output);
            Check(!output.Ready&&output.Failure=="","Capability loss retires the current stereo epoch");
            Check(Graphics.Ordinary==1&&SceneEyeOutput.Ordinary==0,"Capability loss has exactly one normal-image fallback");
            SteamLinkCoverageBridge.CanAcceptOrdinaryRgba=true;var fresh=Enter(head);
            Check(!ReferenceEquals(fresh,output),"Capability regain needs a fresh complete stereo cohort");
        }
        foreach(bool? focus in new bool?[]{true,false,null})
        {
            head=Fresh();VRSession.InputFocus=focus;output=Enter(head);Render(output,head);
            Check(output.Ready&&SceneEyeOutput.Ordinary==1,"Independent coverage does not depend on legacy menu focus");
        }
        SteamLinkSceneRuntime.Reset();Check(!SteamLinkCoverageBridge.CanAcceptOrdinaryRgba,"Reset revokes source capability");
    }
    public static void Main(string[] args)
    {
        CapabilityAdmission();
        var cohortHead=Fresh();var cohort=Enter(cohortHead,false);
        // A component introduced after Left already finished sees only Right.
        cohortHead.stereoActiveEye=Camera.MonoOrStereoscopicEye.Right;Render(cohort,cohortHead);
        Check(SceneEyeOutput.Ordinary==0&&Graphics.Ordinary==1,"mid-pair Right must stay ordinary");
        Time.frameCount++;
        cohortHead.stereoActiveEye=Camera.MonoOrStereoscopicEye.Left;Render(cohort,cohortHead);
        cohortHead.stereoActiveEye=Camera.MonoOrStereoscopicEye.Right;Render(cohort,cohortHead);
        Check(SceneEyeOutput.Ordinary==0&&Graphics.Ordinary==3,"following complete pair must also stay ordinary");
        Time.frameCount++;
        cohortHead.stereoActiveEye=Camera.MonoOrStereoscopicEye.Right;Render(cohort,cohortHead);
        cohortHead.stereoActiveEye=Camera.MonoOrStereoscopicEye.Left;Render(cohort,cohortHead);
        Check(SceneEyeOutput.Ordinary==2&&Graphics.Ordinary==3,"next frame publishes both eyes regardless of order");
        cohortHead=Fresh();cohort=Enter(cohortHead,false);
        Render(cohort,cohortHead);Render(cohort,cohortHead);Time.frameCount++;
        cohortHead.stereoActiveEye=Camera.MonoOrStereoscopicEye.Right;Render(cohort,cohortHead);Time.frameCount++;
        Render(cohort,cohortHead);
        Check(SceneEyeOutput.Ordinary==0,"duplicate Left then Right on another frame never forms a complete cohort");
        cohortHead.stereoActiveEye=Camera.MonoOrStereoscopicEye.Left;Render(cohort,cohortHead);
        Check(SceneEyeOutput.Ordinary==0,"completing the cohort cannot activate the final eye of that same frame");
        Time.frameCount++;Render(cohort,cohortHead);
        Check(SceneEyeOutput.Ordinary==1,"only the next frame can activate after reordered complete cohort");

        var head=Fresh();head.stereoEnabled=false;
        Check(!SteamLinkSceneRuntime.TryEnter(head),"startup non-stereo head must not arm a coverage source");
        Check(head.gameObject.Components==0&&!SteamLinkSceneRuntime.Active,"non-stereo creates no source component");
        head.stereoEnabled=true;var output=Enter(head);Render(output,head);
        Check(SceneEyeOutput.Ordinary==1&&Graphics.Ordinary==0,"stereo readiness immediately recovers without reset");
        foreach(var target in new[]{StereoTargetEyeMask.None,StereoTargetEyeMask.Left,StereoTargetEyeMask.Right})
        {
            head=Fresh();head.stereoTargetEye=target;
            Check(!SteamLinkSceneRuntime.TryEnter(head),"single-eye startup must not arm a coverage source: "+target);
            Check(head.gameObject.Components==0,"single-eye creates no source component: "+target);
            head.stereoTargetEye=StereoTargetEyeMask.Both;output=Enter(head);Render(output,head);
            Check(SceneEyeOutput.Ordinary==1,"Both readiness resumes after "+target);
        }
        head=Fresh();output=Enter(head);head.stereoEnabled=false;Render(output,head);
        Check(!output.Ready&&output.Failure=="","non-stereo pre-cull closes normally");
        Check(SceneEyeOutput.Ordinary==0&&Graphics.Ordinary==1,"non-stereo pre-cull preserves one ordinary image");
        head.stereoEnabled=true;output=Enter(head);Render(output,head);
        Check(SceneEyeOutput.Ordinary==1,"normal non-stereo close does not poison returning head");

        head=Fresh();output=Enter(head);Invoke(output,"OnPreCull");head.stereoEnabled=false;Image(output);
        Check(!output.Ready&&output.Failure=="","XR attachment loss after pre-cull closes normally");
        Check(SceneEyeOutput.Ordinary==0&&Graphics.Ordinary==1,"late attachment loss never packs or duplicates ordinary blit");
        head.stereoEnabled=true;Enter(head);

        head=Fresh();output=Enter(head);head.stereoActiveEye=Camera.MonoOrStereoscopicEye.Mono;Render(output,head);
        Check(!output.Ready&&output.Failure=="","temporary Mono metadata must close without a permanent failure");
        Check(SceneEyeOutput.Ordinary==0&&Graphics.Ordinary==1,"temporary Mono only emits ordinary same callback source");
        Check(VRLog.Warnings.Count==0,"temporary Mono must not report a shader/attachment failure");
        head.stereoActiveEye=Camera.MonoOrStereoscopicEye.Left;output=Enter(head);Render(output,head);
        head.stereoActiveEye=Camera.MonoOrStereoscopicEye.Right;Render(output,head);
        Check(SceneEyeOutput.Ordinary==2,"fresh current left and right render after Mono without explicit reset");
        Check(head.gameObject.Components==2,"Mono recovery replaces the closed epoch exactly once");

        head=Fresh();output=Enter(head,false);head.stereoActiveEye=(Camera.MonoOrStereoscopicEye)42;Render(output,head);
        Check(!output.Ready&&SceneEyeOutput.Ordinary==0,"unknown metadata is never counted as Right for cohort readiness");

        foreach(string stop in new[]{"master-off","preference-off","connection-loss","head-replaced","VR-stop"})
        {
            head=Fresh();output=Enter(head);
            if(stop=="master-off")MixedReality.Enabled.Value=false;
            if(stop=="preference-off")MixedReality.SteamLinkNative.Value=false;
            if(stop=="connection-loss")SteamLinkConnection.Available=false;
            if(stop=="head-replaced")VRCameraPolicy.AllowedHead=new Camera();
            if(stop=="VR-stop")VRSession.IsRunning=false;
            Render(output,head);
            Check(SceneEyeOutput.Ordinary==0&&Graphics.Ordinary==1,"lost ownership must immediately retain ordinary image: "+stop);
            Check(!output.Ready&&output.Failure=="","lost ownership is not a permanent render failure: "+stop);
        }
        head=Fresh();output=Enter(head);head.allowHDR=true;Render(output,head);
        Check(!output.Ready&&output.Failure!="","actual attachment fault remains permanent");
        head.allowHDR=false;Check(!SteamLinkSceneRuntime.TryEnter(head),"actual attachment failure remains latched");
        head=Fresh();output=Enter(head);SceneEyeOutput.Next=SceneEyeResult.SourceFormat;Render(output,head);
        Check(!output.Ready&&output.Failure=="SourceFormat","real output failure remains permanent");
        Check(Graphics.Ordinary==1&&!SteamLinkSceneRuntime.TryEnter(head),"real output failure has one fallback and stays latched");
        SteamLinkSceneRuntime.Reset();Console.WriteLine(checks+" startup/callback assertions passed");
    }
}
