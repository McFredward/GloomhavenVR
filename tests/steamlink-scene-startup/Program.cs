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
        SteamLinkSceneRuntime.Reset();VRLog.Warnings.Clear();SceneEyeOutput.Packed=0;
        SceneEyeOutput.Next=SceneEyeResult.Rendered;Graphics.Ordinary=0;
        MixedReality.Enabled.Value=true;MixedReality.SteamLinkNative.Value=true;
        SteamLinkConnection.Available=true;VRSession.IsRunning=true;VRSession.InputFocus=true;
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
            Check(SceneEyeOutput.Packed==0,"first complete startup cohort must stay ordinary in both eyes");
            Time.frameCount++;head.stereoActiveEye=saved;Graphics.Ordinary=0;
        }
        return output;
    }
    private static void Image(SceneHeadOutput output)
        =>Invoke(output,"OnRenderImage",new RenderTexture(),new RenderTexture());
    private static void Render(SceneHeadOutput output,Camera head)
    { Invoke(output,"OnPreCull");Camera.Begin(head);Image(output);Camera.End(head); }
    private static void DashboardCases()
    {
        foreach(bool? focus in new bool?[]{null,false})
        {
            var head=Fresh();VRSession.InputFocus=focus;
            Check(SteamLinkSceneRuntime.Requested,"dashboard and unknown focus keep the native transport request");
            Check(!SteamLinkSceneRuntime.TryEnter(head),"unfocused or unknown XR focus must not prepare an atlas");
            Check(head.gameObject.Components==0&&!SteamLinkSceneRuntime.Active,"unfocused startup allocates no source");
            Check(VRLog.Warnings.Count==0,"unfocused startup is not a rendering refusal");
            VRSession.InputFocus=true;var output=Enter(head);Render(output,head);
            Check(SceneEyeOutput.Packed==1,"focused startup recovers without explicit Reset");
        }
        var repeatedHead=Fresh();
        for(int cycle=0;cycle!=4;cycle++)
        {
            SceneEyeOutput.Packed=0;Graphics.Ordinary=0;
            var output=Enter(repeatedHead);Render(output,repeatedHead);
            Check(SceneEyeOutput.Packed==1,"focused cycle publishes a current-eye atlas");
            VRSession.InputFocus=false;Render(output,repeatedHead);
            Check(!output.Ready&&output.Failure=="","dashboard ownership loss closes normally");
            Check(SceneEyeOutput.Packed==1&&Graphics.Ordinary==1,"dashboard does not publish another atlas");
            Check(SteamLinkSceneRuntime.Requested&&MixedReality.Enabled.Value&&MixedReality.SteamLinkNative.Value,
                "dashboard must not toggle native MR or its quality-lease request");
            Check(!SteamLinkSceneRuntime.TryEnter(repeatedHead),"dashboard cannot recreate the source while visible");
            Check(VRLog.Warnings.Count==0,"repeated dashboard transitions do not latch a rendering failure");
            VRSession.InputFocus=true;
        }
        // Focus can change after pre-cull, including after the other eye was
        // already submitted. Do not manufacture a stale partner or reuse its
        // cohort; close this epoch and observe a fresh complete pair on return.
        foreach(bool? focus in new bool?[]{false,null})
        {
            var head=Fresh();var output=Enter(head);Render(output,head);
            head.stereoActiveEye=Camera.MonoOrStereoscopicEye.Right;
            Invoke(output,"OnPreCull");VRSession.InputFocus=focus;Image(output);
            Check(!output.Ready&&output.Failure=="","late XR focus loss revokes the epoch without a fault");
            Check(SceneEyeOutput.Packed==1&&Graphics.Ordinary==1,"late XR focus loss never submits a later-eye atlas");
            Check(SteamLinkSceneRuntime.Requested,"late XR focus loss preserves the transport request");
            VRSession.InputFocus=true;Time.frameCount++;
            var resumed=Enter(head,false);
            Render(resumed,head);
            Check(SceneEyeOutput.Packed==1,"resumed mid-pair Right cannot reuse a previous epoch's cohort");
            Time.frameCount++;head.stereoActiveEye=Camera.MonoOrStereoscopicEye.Left;Render(resumed,head);
            head.stereoActiveEye=Camera.MonoOrStereoscopicEye.Right;Render(resumed,head);
            Check(SceneEyeOutput.Packed==1,"resumed complete pair still stays ordinary");
            Time.frameCount++;Render(resumed,head);
            Check(SceneEyeOutput.Packed==2,"resumed atlas begins only on a fresh complete-cohort boundary");
            Check(VRLog.Warnings.Count==0,"late focus recovery needs no failure reset");
        }
        var offHead=Fresh();MixedReality.SteamLinkNative.Value=false;VRSession.InputFocus=false;
        Check(!SteamLinkSceneRuntime.Requested,"explicit native Off remains distinct from dashboard suspension");
        VRSession.InputFocus=true;
        Check(!SteamLinkSceneRuntime.Requested,"focus return never turns a saved Off preference on");
    }
    public static void Main()
    {
        var cohortHead=Fresh();var cohort=Enter(cohortHead,false);
        // A component introduced after Left already finished sees only Right.
        cohortHead.stereoActiveEye=Camera.MonoOrStereoscopicEye.Right;Render(cohort,cohortHead);
        Check(SceneEyeOutput.Packed==0&&Graphics.Ordinary==1,"mid-pair Right must stay ordinary");
        Time.frameCount++;
        cohortHead.stereoActiveEye=Camera.MonoOrStereoscopicEye.Left;Render(cohort,cohortHead);
        cohortHead.stereoActiveEye=Camera.MonoOrStereoscopicEye.Right;Render(cohort,cohortHead);
        Check(SceneEyeOutput.Packed==0&&Graphics.Ordinary==3,"following complete pair must also stay ordinary");
        Time.frameCount++;
        cohortHead.stereoActiveEye=Camera.MonoOrStereoscopicEye.Right;Render(cohort,cohortHead);
        cohortHead.stereoActiveEye=Camera.MonoOrStereoscopicEye.Left;Render(cohort,cohortHead);
        Check(SceneEyeOutput.Packed==2&&Graphics.Ordinary==3,"next frame publishes both eyes regardless of order");
        cohortHead=Fresh();cohort=Enter(cohortHead,false);
        Render(cohort,cohortHead);Render(cohort,cohortHead);Time.frameCount++;
        cohortHead.stereoActiveEye=Camera.MonoOrStereoscopicEye.Right;Render(cohort,cohortHead);Time.frameCount++;
        Render(cohort,cohortHead);
        Check(SceneEyeOutput.Packed==0,"duplicate Left then Right on another frame never forms a complete cohort");
        cohortHead.stereoActiveEye=Camera.MonoOrStereoscopicEye.Left;Render(cohort,cohortHead);
        Check(SceneEyeOutput.Packed==0,"completing the cohort cannot activate the final eye of that same frame");
        Time.frameCount++;Render(cohort,cohortHead);
        Check(SceneEyeOutput.Packed==1,"only the next frame can activate after reordered complete cohort");

        var head=Fresh();head.stereoEnabled=false;
        Check(!SteamLinkSceneRuntime.TryEnter(head),"startup non-stereo head must not arm a packed source");
        Check(head.gameObject.Components==0&&!SteamLinkSceneRuntime.Active,"non-stereo creates no source component");
        head.stereoEnabled=true;var output=Enter(head);Render(output,head);
        Check(SceneEyeOutput.Packed==1&&Graphics.Ordinary==0,"stereo readiness immediately recovers without reset");
        foreach(var target in new[]{StereoTargetEyeMask.None,StereoTargetEyeMask.Left,StereoTargetEyeMask.Right})
        {
            head=Fresh();head.stereoTargetEye=target;
            Check(!SteamLinkSceneRuntime.TryEnter(head),"single-eye startup must not arm a packed source: "+target);
            Check(head.gameObject.Components==0,"single-eye creates no source component: "+target);
            head.stereoTargetEye=StereoTargetEyeMask.Both;output=Enter(head);Render(output,head);
            Check(SceneEyeOutput.Packed==1,"Both readiness resumes after "+target);
        }
        head=Fresh();output=Enter(head);head.stereoEnabled=false;Render(output,head);
        Check(!output.Ready&&output.Failure=="","non-stereo pre-cull closes normally");
        Check(SceneEyeOutput.Packed==0&&Graphics.Ordinary==1,"non-stereo pre-cull preserves one ordinary image");
        head.stereoEnabled=true;output=Enter(head);Render(output,head);
        Check(SceneEyeOutput.Packed==1,"normal non-stereo close does not poison returning head");

        head=Fresh();output=Enter(head);Invoke(output,"OnPreCull");head.stereoEnabled=false;Image(output);
        Check(!output.Ready&&output.Failure=="","XR attachment loss after pre-cull closes normally");
        Check(SceneEyeOutput.Packed==0&&Graphics.Ordinary==1,"late attachment loss never packs or duplicates ordinary blit");
        head.stereoEnabled=true;Enter(head);

        head=Fresh();output=Enter(head);head.stereoActiveEye=Camera.MonoOrStereoscopicEye.Mono;Render(output,head);
        Check(!output.Ready&&output.Failure=="","temporary Mono metadata must close without a permanent failure");
        Check(SceneEyeOutput.Packed==0&&Graphics.Ordinary==1,"temporary Mono only emits ordinary same callback source");
        Check(VRLog.Warnings.Count==0,"temporary Mono must not report a shader/attachment failure");
        head.stereoActiveEye=Camera.MonoOrStereoscopicEye.Left;output=Enter(head);Render(output,head);
        head.stereoActiveEye=Camera.MonoOrStereoscopicEye.Right;Render(output,head);
        Check(SceneEyeOutput.Packed==2,"fresh current left and right render after Mono without explicit reset");
        Check(head.gameObject.Components==2,"Mono recovery replaces the closed epoch exactly once");

        head=Fresh();output=Enter(head,false);head.stereoActiveEye=(Camera.MonoOrStereoscopicEye)42;Render(output,head);
        Check(!output.Ready&&SceneEyeOutput.Packed==0,"unknown metadata is never counted as Right for cohort readiness");

        foreach(string stop in new[]{"master-off","preference-off","connection-loss","head-replaced","VR-stop"})
        {
            head=Fresh();output=Enter(head);
            if(stop=="master-off")MixedReality.Enabled.Value=false;
            if(stop=="preference-off")MixedReality.SteamLinkNative.Value=false;
            if(stop=="connection-loss")SteamLinkConnection.Available=false;
            if(stop=="head-replaced")VRCameraPolicy.AllowedHead=new Camera();
            if(stop=="VR-stop")VRSession.IsRunning=false;
            Render(output,head);
            Check(SceneEyeOutput.Packed==0&&Graphics.Ordinary==1,"lost ownership must immediately retain ordinary image: "+stop);
            Check(!output.Ready&&output.Failure=="","lost ownership is not a permanent render failure: "+stop);
        }
        head=Fresh();output=Enter(head);head.allowHDR=true;Render(output,head);
        Check(!output.Ready&&output.Failure!="","actual attachment fault remains permanent");
        head.allowHDR=false;Check(!SteamLinkSceneRuntime.TryEnter(head),"actual attachment failure remains latched");
        head=Fresh();output=Enter(head);SceneEyeOutput.Next=SceneEyeResult.SourceFormat;Render(output,head);
        Check(!output.Ready&&output.Failure=="SourceFormat","real pack failure remains permanent");
        Check(Graphics.Ordinary==1&&!SteamLinkSceneRuntime.TryEnter(head),"real pack failure has one fallback and stays latched");
        DashboardCases();
        SteamLinkSceneRuntime.Reset();Console.WriteLine(checks+" startup/callback assertions passed");
    }
}
