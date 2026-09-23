using System;
using GloomhavenVR.Net;
using GloomhavenVR.WorldUI;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.Rig;
using UnityEngine;

public static class InteractionProgram
{
    private static int count;
    private static void Check(bool value,string message){count++;if(!value)throw new Exception(message);}
    private static void Near(float actual,float expected,string message)=>Check(Mathf.Abs(actual-expected)<.001f,message+" actual="+actual+" expected="+expected);
    private static Transform Child(Transform parent,string name,Vector3 position)
    {var t=new GameObject(name).transform;t.SetParent(parent,false);t.localPosition=position;return t;}
    private static Transform Rig(out Transform head,out Transform left,out Transform right,out SkinnedMeshRenderer[] skins)
    {
        var root=new GameObject("Station").transform;
        head=Child(root,"Head",new Vector3(0,1.6f,.65f));
        left=Child(head,"EyeLeft",new Vector3(-.032f,0,.07f));right=Child(head,"EyeRight",new Vector3(.032f,0,.07f));
        skins=new SkinnedMeshRenderer[3];
        for(int n=0;n<3;n++)
        {
            var mesh=new Mesh();mesh.vertices=new[]{Vector3.zero,Vector3.right,Vector3.up};mesh.triangles=new[]{0,1,2};
            foreach(string shape in TownServiceFaceRig.ShapeNames)mesh.AddBlendShapeFrame(shape,100,new[]{Vector3.forward,Vector3.forward,Vector3.forward},null,null);
            skins[n]=Child(root,"LOD"+n,Vector3.zero).gameObject.AddComponent<SkinnedMeshRenderer>();skins[n].sharedMesh=mesh;
        }
        return root;
    }
    public static int Run()
    {
        string[] arguments=Environment.GetCommandLineArgs();int bundleArgument=Array.IndexOf(arguments,"-faceBundle");
        if(Array.IndexOf(arguments,"-faceBundleOnly")>=0)
        {
            int outputArgument=Array.IndexOf(arguments,"-faceEvidence");
            Check(bundleArgument>=0&&bundleArgument+1<arguments.Length&&outputArgument>=0&&outputArgument+1<arguments.Length,"actual bundle evidence arguments are complete");
            return ActualPrefabs.Run(arguments[bundleArgument+1],arguments[outputArgument+1]);
        }
        FaceClock.Now=0;RemoteTownFaces.Reset();NetAvatarDriver.Heads.Clear();TownServiceMirror.RemoteSessions.Clear();
        var root=Rig(out var head,out var left,out var right,out var skins);
        var rig=new TownServiceFaceRig(root);Check(rig.Ready,"anatomical pivots discovered");
        var pose=new TownServiceFacePose{HeadYaw=20,LeftYaw=5,RightYaw=-5,BlinkLeft=.75f,JawOpen=.3f};
        rig.Apply(in pose);Check(Quaternion.Angle(head.rotation,Quaternion.identity)>19,"head rotation follows bounded pose");
        foreach(var skin in skins){Near(skin.GetBlendShapeWeight(0),75,"all facial LODs blink");Near(skin.GetBlendShapeWeight(2),30,"all facial LODs mouth");}
        int writes=FaceWrites.Count;rig.BeforeBodySample();rig.Apply(in pose);Check(FaceWrites.Count==writes,"unchanged weights do not dirty every LOD");
        rig.BeforeBodySample();Check(Quaternion.Angle(head.localRotation,Quaternion.identity)<.001f,"body base restored before next facial sample");
        for(int n=0;n<200;n++){rig.Apply(in pose);rig.BeforeBodySample();}
        Check(Quaternion.Angle(head.localRotation,Quaternion.identity)<.001f,"head delta never accumulates");
        var state=default(TownFacePose);
        for(int n=0;n<300;n++)state=TownServiceFaceMotion.Aim(Quaternion.identity,1f,head.position,left.position,right.position,head.position+Vector3.forward*.8f,in state,1f/90f);
        Check(state.LeftYaw>0&&state.RightYaw<0,"eyes converge on common nearby target");
        for(int n=0;n<300;n++)state=TownServiceFaceMotion.Aim(Quaternion.identity,1f,head.position,left.position,right.position,head.position+new Vector3(20,20,-20),in state,1f/90f);
        Check(Mathf.Abs(state.HeadYaw)<=50&&Mathf.Abs(state.HeadPitch)<=22&&Mathf.Abs(state.LeftYaw)<=25&&Mathf.Abs(state.LeftPitch)<=15,"all anatomical limits bounded");
        float maxBlink=0;for(int n=0;n<2000;n++){float blink=TownServiceFaceMotion.Blink(n/90f,1);maxBlink=Mathf.Max(maxBlink,blink);Check(blink>=0&&blink<=1,"blink bounded");}
        Check(maxBlink>.98f,"complete blink reaches closed lids");
        bool different=false;for(int n=0;n<1000;n++)different |= Mathf.Abs(TownServiceFaceMotion.Blink(n/90f,1)-TownServiceFaceMotion.Blink(n/90f,2))>.1f;Check(different,"service blinks not locked together");
        state=default;var evaluated=TownServiceFaceMotion.Evaluate(in state,1,1,Vector3.one);Near(evaluated.JawOpen,0,"silent NPC cannot jaw flap");
        state.Cue=1;evaluated=TownServiceFaceMotion.Evaluate(in state,1,1,new Vector3(.8f,.3f,.6f));Near(evaluated.JawOpen,.8f,"voiced mouth uses actual curve");
        head.rotation=Quaternion.Euler(12,180,26);
        left.rotation=right.rotation=Quaternion.Euler(0,180,0);
        left.position=head.position+new Vector3(.032f,0,-.07f);right.position=head.position+new Vector3(-.032f,0,-.07f);
        var inwardRig=new TownServiceFaceRig(root);var neutral=default(TownFacePose);
        var forwardTarget=(left.position+right.position)*.5f+Vector3.back*2f;
        var aim=TownServiceFaceMotion.Aim(inwardRig.OpticalRotation,1,head.position,left.position,right.position,forwardTarget,in neutral,.1f);
        Near(aim.HeadYaw,0,"inward NPC optical frame is neutral toward visitor");Near(aim.HeadPitch,0,"imported bone axes do not pitch neutral gaze");
        var nativeRotation=head.rotation;var inwardPose=TownServiceFaceMotion.Evaluate(in aim,0,1,Vector3.zero);inwardRig.Apply(in inwardPose);
        Check(Quaternion.Angle(head.rotation,nativeRotation)<.001f,"imported Head rotation retained for neutral gaze");
        var blinkPose=new TownServiceFacePose{LeftPitch=15,RightPitch=-15,BlinkLeft=1,BlinkRight=1};
        for(int n=7;n<11;n++)Near(blinkPose.Weight(n),0,"closed lids override eye follow");
        inwardRig.BeforeBodySample();head.rotation=Quaternion.identity;left.localRotation=right.localRotation=Quaternion.identity;
        left.localPosition=new Vector3(-.032f,0,.07f);right.localPosition=new Vector3(.032f,0,.07f);
        var packet=new TownFaceState{Active=true,Epoch=42,Sequence=10,Clock=10,Merchant=new TownFacePose{HeadYaw=20,Cue=1,Generation=10}};
        RemoteTownFaces.ObservePresence(2,in packet);Check(RemoteTownFaces.Sample(2,out var shown,out _),"first authority face sample");
        packet.Sequence=9;packet.Merchant.Cue=2;RemoteTownFaces.Observe(2,in packet);
        Check(RemoteTownFaces.Sample(2,out shown,out _)&&shown.Merchant.Cue==1,"older samples cannot replace new cue");
        FaceClock.Now=.067f;packet.Sequence=11;packet.Clock=10.067f;packet.Merchant.HeadYaw=40;RemoteTownFaces.Observe(2,in packet);
        FaceClock.Now+=.0335f;Check(RemoteTownFaces.Sample(2,out shown,out var elapsed)&&shown.Merchant.HeadYaw>29&&shown.Merchant.HeadYaw<31,"remote interpolates actual adjacent angles");
        Near(shown.Clock,10.1005f,"blink clock advances independently at render rate");
        FaceClock.Now=4;Check(!RemoteTownFaces.Sample(2,out shown,out _),"stale facial stream expires");
        Check(RemoteTownFaces.Newer(1,uint.MaxValue)&&!RemoteTownFaces.Newer(uint.MaxValue,1),"sequence wrap uses bounded serial arithmetic");
        FaceClock.Now=4.1f;packet.Epoch=43;packet.Sequence=1;packet.Clock=1;packet.Merchant.Generation=1;
        RemoteTownFaces.ObservePresence(2,in packet);
        var old=packet;old.Epoch=42;old.Sequence=100;old.Clock=100;old.Merchant.Generation=100;
        RemoteTownFaces.Observe(2,in old);
        Check(RemoteTownFaces.Sample(2,out shown,out _)&&shown.Epoch==43&&shown.Merchant.Generation==1,"old process cannot mutate new epoch voice");
        RemoteTownFaces.ObservePresence(2,in old);
        Check(RemoteTownFaces.Sample(2,out shown,out _)&&shown.Epoch==43,"retired recovery presence cannot restore old process");
        Check(RemoteTownFaces.TrySeed(out var seeded,out int seedAuthor,out _)&&seedAuthor==2&&seeded.Merchant.Generation==1,"new author can adopt current expression and utterance");
        RemoteTownFaces.Forget(2);Check(!RemoteTownFaces.Sample(2,out _,out _),"departed author cannot retain face");
        RemoteTownFaces.ObservePresence(2,in old);Check(!RemoteTownFaces.Sample(2,out _,out _),"retired process cannot resurrect after disconnect");
        packet.Sequence=2;packet.Clock=1.1f;RemoteTownFaces.Observe(2,in packet);
        Check(!RemoteTownFaces.Sample(2,out _,out _),"fast packet cannot revive suspended peer");
        RemoteTownFaces.ObservePresence(2,in packet);
        Check(RemoteTownFaces.Sample(2,out shown,out _)&&shown.Epoch==43,"same live epoch resumes after temporary network stall");
        packet.Sequence=3;packet.Clock=1.2f;RemoteTownFaces.Observe(2,in packet);
        Check(RemoteTownFaces.Sample(2,out shown,out _)&&shown.Sequence==3,"resumed authority fast stream advances");
        RemoteTownFaces.Forget(2);packet.Sequence=2;packet.Clock=1.1f;RemoteTownFaces.ObservePresence(2,in packet);
        Check(!RemoteTownFaces.Sample(2,out _,out _),"old recovery presence cannot resume suspended peer");
        for(int player=3;player<25;player++)
        {
            packet.Epoch=(uint)(100+player);packet.Sequence=1;packet.Clock=1;
            RemoteTownFaces.ObservePresence(player,in packet);
            Check(RemoteTownFaces.Sample(player,out _,out _),"retired tombstone capacity permits new participant");
            RemoteTownFaces.Forget(player);
        }
        var camera=new GameObject("HMD").AddComponent<Camera>();VRRigDriver.HeadCamera=camera;camera.transform.position=head.position+new Vector3(1,0,2);
        NetPlayerActors.Local=1;FaceClock.Now=0;var attention=new TownServiceFaceAttention();
        Check(attention.Select(1,root,Quaternion.identity,head.position).HasValue,"valid local headset is target");
        NetAvatarDriver.Heads[2]=head.position+new Vector3(-1,0,2);
        TownServiceMirror.RemoteSessions[2]=new TownServiceSessionInfo{Active=true,Service=1,ReceivedTime=2};
        FaceClock.Now=2;var selected=attention.Select(1,root,Quaternion.identity,head.position);
        Check(selected.HasValue&&selected.Value.x<0,"active visitor preferred over equally close spectator");
        NetAvatarDriver.Heads.Clear();VRRigDriver.HeadCamera=null;FaceClock.Now=4;
        Check(!attention.Select(1,root,Quaternion.identity,head.position).HasValue,"missing headset and departed peer return neutral");
        VRRigDriver.HeadCamera=camera;camera.transform.position=head.position+Vector3.forward*2;
        var blocker=GameObject.CreatePrimitive(PrimitiveType.Cube);blocker.transform.position=head.position+Vector3.forward;
        blocker.transform.localScale=new Vector3(.5f,.5f,.2f);Physics.SyncTransforms();
        var sight=new TownServiceFaceAttention();FaceClock.Now+=2;
        Check(!sight.Select(1,root,Quaternion.identity,head.position).HasValue,"native scenery blocks gaze election");
        int scans=NetAvatarDriver.HeadScans;
        for(int n=0;n<90;n++)sight.Select(1,root,Quaternion.identity,head.position);
        Check(NetAvatarDriver.HeadScans==scans,"no-target retry does not scan or raycast at render frequency");
        FaceClock.Now+=.21f;sight.Select(1,root,Quaternion.identity,head.position);
        Check(NetAvatarDriver.HeadScans==scans+1,"no-target retry is bounded but recovers promptly");
        blocker.layer=27;FaceClock.Now+=2;
        Check(sight.Select(1,root,Quaternion.identity,head.position).HasValue,"mod UI does not falsely occlude player gaze");
        UnityEngine.Object.DestroyImmediate(blocker);camera.transform.position=head.position+Vector3.back*2;FaceClock.Now+=2;
        Check(!sight.Select(1,root,Quaternion.identity,head.position).HasValue,"player behind NPC never causes backward stare");
        var face=new TownServiceFace(root,1);VRRigDriver.HeadCamera=camera;camera.transform.position=head.position+new Vector3(4,0,2);
        var remote=new TownFacePose{HeadYaw=-30,Cue=1,Generation=1,SpeechAge=.4f};
        TownServiceFaceSpeech.Curve=(service,cue,age)=>new Vector3(age,0,0);
        var output=face.Tick(false,true,2,in remote,.1f,2);Near(output.HeadYaw,-30,"remote ignores observer headset");Near(output.SpeechAge,.5f,"voice age advances on observer");
        face.BeforeBodySample();camera.transform.position=head.position+new Vector3(-4,0,2);output=face.Tick(false,true,2,in remote,.1f,2);Near(output.HeadYaw,-30,"observer movement cannot alter author pose");
        var second=Rig(out _,out _,out _,out var secondSkins);var otherFace=new TownServiceFace(second,1);otherFace.Tick(false,true,2,in remote,.1f,2);
        Near(secondSkins[0].GetBlendShapeWeight(2),skins[0].GetBlendShapeWeight(2),"two observers share exact mouth curve");
        float before=output.HeadYaw;face.BeforeBodySample();FaceClock.Now+=.1f;
        output=face.Tick(false,false,2,in remote,0,2.1f);Near(output.HeadYaw,before,"short missing face interval retains head");
        face.BeforeBodySample();FaceClock.Now+=1f;output=face.Tick(false,false,2,in remote,0,3);
        Check(output.HeadYaw>before&&output.HeadYaw<0,"missing face returns smoothly without observer election");
        float beforeHandover=output.HeadYaw;face.Seed(in remote,2,.2f);face.BeforeBodySample();output=face.Tick(true,false,1,in remote,0,3);
        Check(Mathf.Abs(output.HeadYaw-beforeHandover)<1,"authority handover preserves pose before smooth attention change");
        TownServiceFaceSpeech.Curve=null;TownServiceFaceSpeech.Observer=null;TownServiceFaceSpeech.Sampler=null;
        UnityEngine.Object.DestroyImmediate(root.gameObject);UnityEngine.Object.DestroyImmediate(second.gameObject);UnityEngine.Object.DestroyImmediate(camera.gameObject);
        if(bundleArgument>=0)
        {
            int outputArgument=Array.IndexOf(arguments,"-faceEvidence");
            Check(bundleArgument+1<arguments.Length&&outputArgument>=0&&outputArgument+1<arguments.Length,"actual bundle evidence arguments are complete");
            count+=ActualPrefabs.Run(arguments[bundleArgument+1],arguments[outputArgument+1]);
        }
        return count;
    }
}
