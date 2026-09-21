using System;
using System.Linq;
using GloomhavenVR.Net;
using GloomhavenVR.WorldUI;
using UnityEngine;
public static class InteractionProgram
{
    private static int count;
    private static void Check(bool condition,string message){count++;if(!condition)throw new Exception(message);}
    private static void Near(float a,float b,float epsilon,string message)=>Check(Mathf.Abs(a-b)<epsilon,message+": "+a+" / "+b);
    public static int Main()
    {try{Console.WriteLine(Run()+" production assertions");return 0;}catch(Exception error){Console.WriteLine(error);return 1;}}
    public static int Run()
    {
        var original=new TownActivityPose{WorkClock=2,TransitionAge=.65f};
        TownServiceActivityMotion.Engage(ref original,true);
        var direct=TownServiceActivityMotion.Advance(original,.45f);
        var fine=original;for(int n=0;n<45;n++)fine=TownServiceActivityMotion.Advance(fine,.01f);
        Near(direct.WorkClock,fine.WorkClock,.00001f,"analytic phase independent of render frequency");
        Near(TownServiceActivityMotion.Blend(in direct),TownServiceActivityMotion.Blend(in fine),.00001f,"analytic blend independent of render frequency");
        float before=TownServiceActivityMotion.Blend(in direct);TownServiceActivityMotion.Engage(ref direct,false);
        Near(before,TownServiceActivityMotion.Blend(in direct),.000001f,"interrupted transition keeps current pose");
        var engaged=TownServiceActivityMotion.Advance(original,1f);float clock=engaged.WorkClock;
        engaged=TownServiceActivityMotion.Advance(engaged,10f);Near(engaged.WorkClock,clock,.00001f,"engaged occupation remains paused");
        TownServiceActivityMotion.Engage(ref engaged,false);var resumed=TownServiceActivityMotion.Advance(engaged,1f);
        Check(resumed.WorkClock>clock&&resumed.WorkClock<clock+1,"resume advances preserved workclock smoothly");
        for(byte service=1;service<=3;service++)
        {
            var phase=new TownActivityPose{TransitionAge=.65f};Vector3 previous=Vector3.zero;
            for(int n=0;n<4000;n++)
            {
                if(n==300||n==2000)TownServiceActivityMotion.Engage(ref phase,true);
                if(n==400||n==2700)TownServiceActivityMotion.Engage(ref phase,false);
                phase=TownServiceActivityMotion.Advance(phase,1f/90f);
                TownServiceActivityMotion.Hands(service,in phase,out var left,out var right,out var curl);
                if(n>0)Check(Vector3.Distance(previous,right)<.018f,"no handtarget jump at loop or interruption");
                Check(left.y>=1&&right.y>=1&&curl>=0&&curl<=1,"bounded occupation contacts");previous=right;
            }
        }
        FaceClock.Now=0;RemoteTownActivities.Reset();
        var state=new TownActivityState{Active=true,Epoch=1,Sequence=5,Clock=2,Merchant=original,Temple=original,Enchantress=original};
        RemoteTownActivities.Observe(2,in state);Check(!RemoteTownActivities.Sample(2,out _,out _),"fast stream cannot establish epoch");
        RemoteTownActivities.ObservePresence(2,in state);FaceClock.Now=.2f;
        Check(RemoteTownActivities.Sample(2,out var observed,out _),"presence starts occupation timeline");
        var expected=TownServiceActivityMotion.Advance(original,.2f);Near(observed.Merchant.WorkClock,expected.WorkClock,.00001f,"remote evaluates every intermediate workphase");
        var stale=state;stale.Sequence=4;stale.Merchant.WorkClock=99;
        RemoteTownActivities.Observe(2,in stale);FaceClock.Now+=.1f;RemoteTownActivities.Sample(2,out observed,out _);
        expected=TownServiceActivityMotion.Advance(original,.3f);
        Near(observed.Merchant.WorkClock,expected.WorkClock,.00001f,"older occupation cannot replace current phase");
        RemoteTownActivities.Forget(2);state.Sequence=6;RemoteTownActivities.Observe(2,in state);Check(!RemoteTownActivities.Sample(2,out _,out _),"fastpacket cannot resurrect suspended activity");
        RemoteTownActivities.ObservePresence(2,in state);Check(RemoteTownActivities.Sample(2,out _,out _),"same epoch recovers after networkstall");
        var changed=state;changed.Epoch=2;changed.Sequence=1;RemoteTownActivities.ObservePresence(2,in changed);
        state.Sequence=7;RemoteTownActivities.ObservePresence(2,in state);RemoteTownActivities.Sample(2,out observed,out _);Check(observed.Epoch==2,"retired activity epoch cannot return");
        Paired();Handover();
        string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-faceBundle");
        if(at>=0)Actual(args[at+1]);
        return count;
    }
    private static void Paired()
    {
        RemoteTownActivities.Reset(); RemoteTownFaces.Reset(); FaceClock.Now=0;
        var pose=new TownActivityPose{WorkClock=3,TransitionAge=.65f};
        var activity=new TownActivityState{Active=true,Epoch=9,Sequence=1,Clock=5,Merchant=pose,Temple=pose,Enchantress=pose};
        var expression=new TownFacePose{HeadYaw=0,Cue=0,Generation=0,SpeechAge=0,Jaw=0,Wide=0,Round=0};
        var face=new TownFaceState{Active=true,Epoch=9,Sequence=1,Clock=5,Merchant=expression,Temple=expression,Enchantress=expression};
        Check(RemoteTownPerformance.Observe(2,in activity,in face,true),"paired presence establishes both timelines");
        FaceClock.Now=.08f;
        RemoteTownActivities.Sample(2,out var beforeBody,out _); RemoteTownFaces.Sample(2,out var beforeFace,out _);
        activity.Sequence=2;activity.Clock=5.066f;activity.Merchant.WorkClock=3.066f;
        face.Sequence=2;face.Clock=activity.Clock;face.Merchant.HeadYaw=20;
        Check(RemoteTownPerformance.Observe(2,in activity,in face,false),"matching paired stream accepted");
        RemoteTownActivities.Sample(2,out var body,out _);RemoteTownFaces.Sample(2,out var head,out _);
        Near(body.Merchant.WorkClock,beforeBody.Merchant.WorkClock,.00001f,"packet jitter does not jump occupation at arrival");
        Near(head.Merchant.HeadYaw,beforeFace.Merchant.HeadYaw,.00001f,"packet jitter does not jump gaze at arrival");
        FaceClock.Now+=.033f;RemoteTownActivities.Sample(2,out body,out _);RemoteTownFaces.Sample(2,out head,out _);
        Near(head.Merchant.HeadYaw,10,.001f,"head at correlated half reconciliation interval");
        Near(body.Merchant.WorkClock,(beforeBody.Merchant.WorkClock+activity.Merchant.WorkClock)*.5f,.0001f,"body at correlated half reconciliation interval");
        Near(body.Clock,head.Clock,.00001f,"face and body share expression clock");
        var legacy=face;legacy.Sequence=100;
        Check(!RemoteTownPerformance.ObserveLegacyFace(2,in legacy),"legacy face cannot advance paired epoch alone");
        var nextBody=activity;nextBody.Sequence=3;
        var mismatched=face;mismatched.Sequence=4;
        Check(!RemoteTownPerformance.Observe(2,in nextBody,in mismatched,false),"mismatched sequence cannot partially advance pair");
        var older=activity;older.Sequence=1;
        Check(!RemoteTownPerformance.Observe(2,in older,in face,false),"reordered half cannot partially advance pair");
        activity.Sequence=3;face.Sequence=3;activity.Clock+=.066f;face.Clock=activity.Clock;
        Check(RemoteTownPerformance.Observe(2,in activity,in face,false),"pair remains live after incompatible standalone packet");
        RemoteTownActivities.Forget(2);RemoteTownFaces.Forget(2);activity.Sequence=4;face.Sequence=4;
        Check(!RemoteTownPerformance.Observe(2,in activity,in face,false),"fast pair cannot revive suspension");
        Check(RemoteTownPerformance.Observe(2,in activity,in face,true),"presence pair resumes same process epoch");
    }
    private static void Handover()
    {
        var transition=new TownServiceActivityHandover();
        var working=new TownActivityPose{WorkClock=1000,TransitionAge=.65f};
        var visiting=new TownActivityPose{WorkClock=8,TransitionAge=.65f,Engaged=true};
        var a=TownServiceActivityMotion.Visual(1,in working);var b=TownServiceActivityMotion.Visual(1,in visiting);
        var faceA=new TownFacePose{HeadYaw=-30,LeftYaw=-8,RightYaw=-7};
        var faceB=new TownFacePose{HeadYaw=35,LeftYaw=10,RightYaw=9};
        transition.Sample(2,2,.01f,in a,in faceA,out var shown,out var head);
        transition.Sample(1,1,.01f,in b,in faceB,out shown,out head);
        Near(Vector3.Distance(shown.Right,a.Right),0,.00001f,"returning authority keeps displayed hands at first frame");
        Near(head.HeadYaw,faceA.HeadYaw,.00001f,"returning authority keeps displayed gaze at first frame");
        transition.Sample(1,1,.175f,in b,in faceB,out shown,out head);
        Near(Vector3.Distance(shown.Right,Vector3.Lerp(a.Right,b.Right,.5f)),0,.00001f,"recovery blends evaluated contact rather than distant work clocks");
        Near(head.HeadYaw,Mathf.Lerp(faceA.HeadYaw,faceB.HeadYaw,.5f),.0001f,"face and body share authority recovery interval");
        var interrupted=shown;var interruptedHead=head;
        transition.Sample(3,3,.01f,in a,in faceA,out shown,out head);
        Near(Vector3.Distance(shown.Right,interrupted.Right),0,.00001f,"handover interrupted by third author preserves current hands");
        Near(head.HeadYaw,interruptedHead.HeadYaw,.00001f,"handover interrupted by third author preserves current gaze");
        transition.Sample(3,3,.35f,in a,in faceA,out shown,out head);
        Near(Vector3.Distance(shown.Right,a.Right),0,.00001f,"recovery reaches new authority contact");
        Near(head.HeadYaw,faceA.HeadYaw,.00001f,"recovery reaches new authority gaze");
        transition.Sample(3,4,.01f,in b,in faceB,out shown,out head);
        Near(Vector3.Distance(shown.Right,a.Right),0,.00001f,"same player new ownership epoch also reconciles");
    }
    private static void Actual(string path)
    {
        AssetBundle bundle=AssetBundle.LoadFromFile(path);Check(bundle!=null,"actual bundle loads");
        try
        {
            foreach(string npc in new[]{"merchant","priestess","enchantress"})
            {
                byte service=npc=="merchant"?(byte)1:npc=="priestess"?(byte)2:(byte)3;
                string asset=bundle!.GetAllAssetNames().Single(n=>n.EndsWith("/town"+npc+".prefab"));
                GameObject obj=UnityEngine.Object.Instantiate(bundle.LoadAsset<GameObject>(asset));
                try
                {
                    Transform root=obj.transform;root.position=new Vector3(2,1,3);root.rotation=Quaternion.Euler(0,37,0);root.localScale=Vector3.one*.8f;
                    var grounding=new TownServiceGrounding(root);grounding.Apply(0f,-.02f);
                    var rig=new TownServiceActivityRig(root,service);Check(rig.Ready,"actual imported arms found");
                    Animation animation=root.GetComponentInChildren<Animation>();
                    Transform hand=root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Hand.R");
                    Transform upper=root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="UpperArm.R");
                    Transform fore=root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Forearm.R");
                    Transform[] thumbs=root.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("Thumb")).ToArray();
                    Quaternion[] thumbNeutral=thumbs.Select(t=>t.localRotation).ToArray();
                    var phase=new TownActivityPose{TransitionAge=.65f};Quaternion previousHand=Quaternion.identity;
                    for(int n=0;n<2500;n++)
                    {
                        if(n==833)grounding.Apply(.035f,-.02f);if(n==1666)grounding.Apply(-.035f,-.02f);
                        rig.BeforeBodySample();animation.Stop();var body=animation["Idle"];body.enabled=true;body.weight=1;body.time=n/90f;animation.Sample();body.enabled=false;
                        Quaternion before=upper.localRotation;
                        if(n==500)TownServiceActivityMotion.Engage(ref phase,true);if(n==1100)TownServiceActivityMotion.Engage(ref phase,false);
                        phase=TownServiceActivityMotion.Advance(phase,1f/90f);
                        TownServiceActivityMotion.Hands(service,in phase,out _,out var target,out _);
                        float reach=Vector3.Distance(upper.position,fore.position)+Vector3.Distance(fore.position,hand.position);
                        Vector3 wanted=root.TransformPoint(target);float excess=Mathf.Max(0,Vector3.Distance(wanted,upper.position)-reach+.001f);
                        rig.Apply(in phase);
                        for(int digit=0;digit<thumbs.Length;digit++)
                            Check(Quaternion.Angle(thumbs[digit].localRotation,thumbNeutral[digit])<3.05f,"approximate thumb stays within supported deformation range");
                        if(service==1&&TownServiceActivityMotion.Writing(phase.WorkClock)>.99f&&TownServiceActivityMotion.Blend(in phase)<.01f)
                            Check(Vector3.Distance(hand.position,wanted)<.003f,"writing contact survives resolved terrain offsets at "+n+": "+Vector3.Distance(hand.position,wanted));
                        if(n>0)Check(Quaternion.Angle(previousHand,hand.rotation)<4f,"hand orientation remains smooth through prayer interruption");
                        previousHand=hand.rotation;
                        Check(Vector3.Distance(hand.position,wanted)<=excess+.0001f,"actual hand reaches occupation target");
                        rig.BeforeBodySample();Check(Quaternion.Angle(upper.localRotation,before)<.05f,"original arm base restores without accumulation");
                    }
                    grounding.Apply(0f,0f);ActivityRender.Render(obj, service, rig);
                }
                finally{UnityEngine.Object.DestroyImmediate(obj);}
            }
        }
        finally{if(bundle!=null)bundle.Unload(true);}
    }
}
