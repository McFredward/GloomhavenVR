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
        SceneEyeOutput.Next=SceneEyeResult.Rendered;SceneEyeOutput.Ordinary=0;Graphics.Ordinary=0;
        MixedReality.Enabled.Value=true;MixedReality.SteamLinkNative.Value=true;
        SteamLinkConnection.Available=true;VRSession.IsRunning=true;VRSession.InputFocus=true;
        MixedReality.SteamLinkEdgeQuality.Value=true;SteamLinkStreamQuality.Prepared=true;SteamLinkStreamQuality.LegacyReleased=true;
        SteamLinkStreamQuality.Width=SteamLinkStreamQuality.Height=4040;
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
            Check(SceneEyeOutput.Packed==0,"first complete startup cohort must stay ordinary in both eyes");
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
        Check(SteamLinkSceneRuntime.TryEnter(head),"fresh full-RGB/legacy source can prepare");
        var output=Current!;
        head.stereoActiveEye=Camera.MonoOrStereoscopicEye.Left;Render(output,head);
        head.stereoActiveEye=Camera.MonoOrStereoscopicEye.Right;Render(output,head);
        Time.frameCount++;head.stereoActiveEye=Camera.MonoOrStereoscopicEye.Left;
        return output;
    }
    private static void OrdinaryBridge()
    {
        var head=Fresh();SteamLinkCoverageBridge.CanAcceptOrdinaryRgba=true;
        SteamLinkStreamQuality.LegacyReleased=false;
        Check(!SteamLinkSceneRuntime.TryEnter(head),"full RGBA waits for actual legacy settings restoration");
        Check(head.gameObject.Components==0,"restoration wait records no source or atlas");
        SteamLinkStreamQuality.LegacyReleased=true;var output=Prime(head);Render(output,head);
        Check(output.OrdinaryRgba&&SceneEyeOutput.Ordinary==1&&SceneEyeOutput.Packed==0,"negotiated source uses ordinary RGBA without an atlas");
        Check(!output.ReadyForProjection,"old atlas projection preview is not admitted for ordinary full-RGB source");
        Check(UnityEngine.XR.XRSettings.eyeTextureWidth==4040&&UnityEngine.XR.XRSettings.eyeTextureHeight==4040,"native coverage never writes eye resolution");
        head.stereoActiveEye=Camera.MonoOrStereoscopicEye.Right;Render(output,head);
        Check(SceneEyeOutput.Ordinary==2&&SceneEyeOutput.Packed==0,"both explicit eyes preserve full RGB");
        SteamLinkCoverageBridge.CanAcceptOrdinaryRgba=false;Invoke(output,"OnPreCull");Image(output);
        Check(!output.Ready&&Graphics.Ordinary==3,"lost capability immediately keeps the ordinary RGB source");
        Check(output.Failure==""&&VRLog.Warnings.Count==0,"capability loss does not poison the head");

        head=Fresh();output=Prime(head);Render(output,head);
        Check(SceneEyeOutput.Packed==1,"legacy requested path remains available");
        SteamLinkCoverageBridge.CanAcceptOrdinaryRgba=true;
        head.stereoActiveEye=Camera.MonoOrStereoscopicEye.Right;Render(output,head);
        Check(SceneEyeOutput.Packed==2&&SceneEyeOutput.Ordinary==0,"mid-pair capability gain cannot change only the second eye");
        Check(!SteamLinkSceneRuntime.TryEnter(head)&&!output.Ready,"cold route transition retires the old stereo epoch");
        output=Prime(head);Render(output,head);
        Check(output.OrdinaryRgba&&SceneEyeOutput.Ordinary==1,"next complete stereo epoch switches to full RGBA");
        SteamLinkSceneRuntime.Reset();Check(!SteamLinkCoverageBridge.CanAcceptOrdinaryRgba,"session reset revokes live source permission");
    }
    private static void MenuAdmission()
    {
        foreach(bool? focus in new bool?[]{null,false})
        {
            var head=Fresh();VRSession.InputFocus=focus;
            Check(SteamLinkSceneRuntime.Requested,"Menu wait retains the native preference rather than selecting chroma key");
            Check(!SteamLinkSceneRuntime.TransportPrepared(),"Legacy menu admission requires explicitly known XR input focus");
            Check(!SteamLinkSceneRuntime.TryEnter(head),"Menu or unknown focus cannot prepare a legacy atlas source");
            Check(Current==null&&head.gameObject.Components==0,"Menu admission creates no legacy source resources");
            Check(SteamLinkStreamQuality.Prepared&&SteamLinkStreamQuality.LegacyReleased,"Menu wait leaves the independent quality lease unchanged");
            Check(VRLog.Warnings.Count==0,"Menu wait is not a permanent source error");
            VRSession.InputFocus=true;
            var output=Prime(head);
            Check(SceneEyeOutput.Packed==0&&Graphics.Ordinary==2,"Focus regain first observes an ordinary complete stereo cohort");
            Render(output,head);head.stereoActiveEye=Camera.MonoOrStereoscopicEye.Right;Render(output,head);
            Check(SceneEyeOutput.Packed==2,"Focus regain resumes both legacy eyes without resetting the preference");
        }
        foreach(bool afterPreCull in new[]{false,true})
        {
            var head=Fresh();var output=Enter(head);
            Render(output,head);
            Check(SceneEyeOutput.Packed==1,"Focused left eye is packed before the menu opens");
            head.stereoActiveEye=Camera.MonoOrStereoscopicEye.Right;
            if(afterPreCull)Invoke(output,"OnPreCull");
            VRSession.InputFocus=false;
            if(!afterPreCull)
            {
                Invoke(output,"OnPreCull");
                Check(!output.Ready,"Menu focus loss before pre-cull immediately revokes source ownership");
            }
            Image(output);
            Check(SceneEyeOutput.Packed==1&&Graphics.Ordinary==1,"Late menu opening cannot pack the other eye from a revoked source");
            Check(!output.Ready&&output.Failure=="","Late menu loss retires the epoch without a permanent failure");
            Check(SteamLinkSceneRuntime.Requested&&SteamLinkStreamQuality.Prepared,"Late menu loss keeps requested and host quality permission");
            Check(!SteamLinkSceneRuntime.TryEnter(head)&&Current==null,"Cold menu tick keeps the source temporarily exited");
            VRSession.InputFocus=true;
            var restored=Prime(head);
            Check(!ReferenceEquals(restored,output)&&SceneEyeOutput.Packed==1,"Restored focus cannot reuse an old eye or ready stereo epoch");
            Render(restored,head);head.stereoActiveEye=Camera.MonoOrStereoscopicEye.Right;Render(restored,head);
            Check(SceneEyeOutput.Packed==3&&VRLog.Warnings.Count==0,"After a fresh complete cohort both restored eyes render normally");
        }
        // No cold tick between loss and regain: the actual rendering callback
        // must close the old owner, rather than keeping its cohort ready.
        {
            var head=Fresh();var output=Enter(head);Invoke(output,"OnPreCull");
            VRSession.InputFocus=false;Image(output);
            Check(!output.Ready,"Render-time menu loss closes before focus can return between cold ticks");
            VRSession.InputFocus=true;
            var restored=Prime(head);
            Check(!ReferenceEquals(restored,output)&&SceneEyeOutput.Packed==0,"Callback-only loss also requires a fresh complete stereo epoch");
            Render(restored,head);Check(SceneEyeOutput.Packed==1,"Callback-only menu close recovers without a reset or fault latch");
        }
        foreach(bool? focus in new bool?[]{null,false})
        {
            var head=Fresh();VRSession.InputFocus=focus;SteamLinkCoverageBridge.CanAcceptOrdinaryRgba=true;
            Check(SteamLinkSceneRuntime.TransportPrepared(),"Independent ordinary RGBA is explicitly exempt from the legacy menu gate");
            var output=Prime(head);Render(output,head);
            VRSession.InputFocus=focus==null?false:null;
            head.stereoActiveEye=Camera.MonoOrStereoscopicEye.Right;Render(output,head);
            Check(output.Ready&&output.OrdinaryRgba&&SceneEyeOutput.Ordinary==2&&SceneEyeOutput.Packed==0,"Both full-RGBA eyes keep their source while the native menu has input focus");
            Check(SteamLinkSceneRuntime.Active&&SteamLinkSceneRuntime.Requested,"Native coverage route stays active beside the Steam menu");
        }
        SteamLinkSceneRuntime.Reset();VRSession.InputFocus=true;
    }
    public static void Main(string[] args)
    {
        if(Array.IndexOf(args,"--ordinary-only")>=0)
        {
            OrdinaryBridge();Console.WriteLine(checks+" ordinary source assertions passed");return;
        }
        if(Array.IndexOf(args,"--menu-only")>=0)
        {
            MenuAdmission();
            Console.WriteLine(checks+" menu/callback assertions passed");return;
        }

        var pendingHead=Fresh();SteamLinkStreamQuality.Prepared=false;
        Check(!SteamLinkSceneRuntime.TryEnter(pendingHead),"Pending host lease must not prepare a source");
        Check(pendingHead.gameObject.Components==0&&!SteamLinkSceneRuntime.Active,"Pending host lease has no active source resources");
        Check(SteamLinkSceneRuntime.Requested,"Waiting host settings must retain native request, never select chroma");
        SteamLinkStreamQuality.Prepared=true;var admitted=Enter(pendingHead);Render(admitted,pendingHead);
        Check(SceneEyeOutput.Packed==1,"Prepared current lease starts only after fresh complete stereo cohort");

        pendingHead=Fresh();admitted=Enter(pendingHead);SteamLinkStreamQuality.Prepared=false;
        Invoke(admitted,"OnPreCull");
        Check(!admitted.Ready,"Host lease loss before pre-cull revokes source ownership immediately");
        Image(admitted);
        Check(SceneEyeOutput.Packed==0&&Graphics.Ordinary==1,"Host lease loss before pre-cull preserves one ordinary image");
        Check(!admitted.Ready&&admitted.Failure=="","Host lease loss is temporary, never a permanent source fault");
        SteamLinkStreamQuality.Prepared=true;admitted=Enter(pendingHead);Render(admitted,pendingHead);
        Check(SceneEyeOutput.Packed==1,"Host lease recovery prepares a fresh source without Reset");

        pendingHead=Fresh();admitted=Enter(pendingHead);Invoke(admitted,"OnPreCull");
        SteamLinkStreamQuality.Prepared=false;Image(admitted);
        Check(SceneEyeOutput.Packed==0&&Graphics.Ordinary==1,"Host completion after pre-cull cannot publish an atlas");

        pendingHead=Fresh();admitted=Enter(pendingHead);
        UnityEngine.XR.XRSettings.eyeTextureWidth=5050;Render(admitted,pendingHead);
        Check(SceneEyeOutput.Packed==0&&Graphics.Ordinary==1,"New XR carrier cannot reuse an old-width host lease");
        Check(!admitted.Ready&&admitted.Failure=="","Host width wait never poisons returning source");
        SteamLinkStreamQuality.Width=5050;admitted=Enter(pendingHead,false);
        // The fixture's image extent must reflect the new actual carrier too.
        Invoke(admitted,"OnPreCull");Invoke(admitted,"OnRenderImage",new RenderTexture{width=5050},new RenderTexture{width=5050});
        Check(SceneEyeOutput.Packed==0,"New host width still requires a fresh complete stereo cohort");

        pendingHead=Fresh();admitted=Enter(pendingHead);Invoke(admitted,"OnPreCull");
        Invoke(admitted,"OnRenderImage",new RenderTexture{width=5050},new RenderTexture{width=5050});
        Check(SceneEyeOutput.Packed==0&&Graphics.Ordinary==1,"Late actual source extent cannot use a host lease for another carrier");
        Check(!admitted.Ready&&admitted.Failure=="","Late source width wait closes without a permanent latch");

        pendingHead=Fresh();SteamLinkStreamQuality.Prepared=false;MixedReality.SteamLinkEdgeQuality.Value=false;
        admitted=Enter(pendingHead);Render(admitted,pendingHead);
        Check(SceneEyeOutput.Packed==1,"Explicit edge-quality opt-out keeps native source usable without a lease");

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
        MenuAdmission();
        SteamLinkSceneRuntime.Reset();Console.WriteLine(checks+" startup/callback assertions passed");
    }
}
