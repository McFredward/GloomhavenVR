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
        count += HandContacts.Run();
        count += AudioSourceChecks.Run();
        var original=new TownActivityPose{WorkClock=2,TransitionAge=TownServiceActivityMotion.TransitionSeconds};
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
            var phase=new TownActivityPose{TransitionAge=TownServiceActivityMotion.TransitionSeconds};Vector3 previous=Vector3.zero;
            for(int n=0;n<4000;n++)
            {
                if(n==300||n==2000)TownServiceActivityMotion.Engage(ref phase,true);
                if(n==400||n==2700)TownServiceActivityMotion.Engage(ref phase,false);
                phase=TownServiceActivityMotion.Advance(phase,1f/90f);
                TownServiceActivityMotion.Hands(service,in phase,out var left,out var right,out var curl);
                if(n>0)Check(Vector3.Distance(previous,right)<.018f,"no handtarget jump at loop or interruption");
                Check(left.y>=.87f&&right.y>=.87f&&curl>=0&&curl<=1,"bounded occupation contacts");previous=right;
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
        Paired();Handover();Choreography();GeneratedMotion();count+=AudioClockChecks.Run();
        string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-faceBundle");
        if(at>=0)Actual(args[at+1]);
        return count;
    }
    private static void Paired()
    {
        RemoteTownActivities.Reset(); RemoteTownFaces.Reset(); FaceClock.Now=0;
        var pose=new TownActivityPose{WorkClock=3,TransitionAge=TownServiceActivityMotion.TransitionSeconds};
        var activity=new TownActivityState{Active=true,Epoch=9,Sequence=1,Clock=5,Merchant=pose,Temple=pose,Enchantress=pose,MerchantOfferingBlend=.4f};
        var expression=new TownFacePose{HeadYaw=0,Cue=0,Generation=0,SpeechAge=0,Jaw=0,Wide=0,Round=0};
        var face=new TownFaceState{Active=true,Epoch=9,Sequence=1,Clock=5,Merchant=expression,Temple=expression,Enchantress=expression};
        Check(RemoteTownPerformance.Observe(2,in activity,in face,true),"paired presence establishes both timelines");
        FaceClock.Now=.08f;
        RemoteTownActivities.Sample(2,out var beforeBody,out _); RemoteTownFaces.Sample(2,out var beforeFace,out _);
        activity.Sequence=2;activity.Clock=5.066f;activity.Merchant.WorkClock=3.066f;activity.MerchantOfferingBlend=.8f;
        face.Sequence=2;face.Clock=activity.Clock;face.Merchant.HeadYaw=20;
        Check(RemoteTownPerformance.Observe(2,in activity,in face,false),"matching paired stream accepted");
        RemoteTownActivities.Sample(2,out var body,out _);RemoteTownFaces.Sample(2,out var head,out _);
        Near(body.Merchant.WorkClock,beforeBody.Merchant.WorkClock,.00001f,"packet jitter does not jump occupation at arrival");
        Near(body.MerchantOfferingBlend,.4f,.00001f,"packet jitter does not jump shared merchant offering pose");
        Near(head.Merchant.HeadYaw,beforeFace.Merchant.HeadYaw,.00001f,"packet jitter does not jump gaze at arrival");
        FaceClock.Now+=.033f;RemoteTownActivities.Sample(2,out body,out _);RemoteTownFaces.Sample(2,out head,out _);
        Near(head.Merchant.HeadYaw,10,.001f,"head at correlated half reconciliation interval");
        Near(body.Merchant.WorkClock,(beforeBody.Merchant.WorkClock+activity.Merchant.WorkClock)*.5f,.0001f,"body at correlated half reconciliation interval");
        Near(body.MerchantOfferingBlend,.6f,.0001f,"merchant offering pose follows authored reconciliation interval");
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
        var working=new TownActivityPose{WorkClock=1000,TransitionAge=TownServiceActivityMotion.TransitionSeconds};
        var visiting=new TownActivityPose{WorkClock=8,TransitionAge=TownServiceActivityMotion.TransitionSeconds,Engaged=true};
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
    private static void Choreography()
    {
        Vector3[] previous=new Vector3[3];
        Vector3 previousMerchantHand=Vector3.zero;
        int motionlessAirFrames=0, longestMotionlessAir=0;
        bool large=false,small=false; float quietSeconds=0f; int experiments=0; bool casting=false;
        float[] strengths=new float[3], modes=new float[3];
        Vector3[] spellPeaks=new Vector3[3];
        float lastExperiment=-100f; Vector3 previousSpell=Vector3.zero;
        for(int frame=0;frame<12960;frame++)
        {
            var state=new TownActivityPose{WorkClock=frame/90f,TransitionAge=TownServiceActivityMotion.TransitionSeconds};
            var merchant=TownServiceActivityMotion.Visual(1,in state);
            if(frame>0&&merchant.Left.y>1.02f&&Vector3.Distance(merchant.CoinGrip,Vector3.zero)>.5f
                &&Vector3.Distance(previousMerchantHand,merchant.Left)<.000001f)
                motionlessAirFrames++;
            else motionlessAirFrames=0;
            longestMotionlessAir=Math.Max(longestMotionlessAir,motionlessAirFrames);
            previousMerchantHand=merchant.Left;
            for(int coin=0;coin<3;coin++)
            {
                float grip=coin==0?merchant.CoinGrip.x:coin==1?merchant.CoinGrip.y:merchant.CoinGrip.z;
                Vector3 seat=coin==0?merchant.Coin0:coin==1?merchant.Coin1:merchant.Coin2;
                Vector3 displayed=Vector3.Lerp(seat,merchant.Left,grip);
                if(frame>0)Check(Vector3.Distance(previous[coin],displayed)<.014f,"counted coin never teleports across pickup/deposit/loop frame="+frame+" coin="+coin+" delta="+Vector3.Distance(previous[coin],displayed));
                Check(grip==0f||grip==1f,"coin is resting or rigidly gripped, never magnetically attracted");
                if(grip==0f)Check(displayed.y>=.957f&&displayed.y<=.965f,"released coins rest on counter/stack");
                previous[coin]=displayed;
            }
            if(TownServiceActivityMotion.MerchantCanAttend(state.WorkClock))
                Check(Vector3.Distance(merchant.CoinGrip,Vector3.zero)==0f,
                    "merchant only greets after releasing the current coin");
            var spell=TownServiceActivityMotion.Visual(3,in state);
            if(spell.Cast>.75f)large=true;
            if(spell.Cast>.45f&&spell.Cast<.7f)small=true;
            if(spell.Cast<.001f)quietSeconds+=1f/90f;
            if(spell.Cast>.25f&&!casting)
            { Check(state.WorkClock-lastExperiment>35f,"spell phrases have long varied quiet intervals");lastExperiment=state.WorkClock;experiments++; }
            int block=frame/(48*90);
            if(spell.Cast>strengths[block]) { strengths[block]=spell.Cast; spellPeaks[block]=spell.Right; }
            modes[block]=Mathf.Max(modes[block],spell.CastSway);
            if(frame>0)Check(Vector3.Distance(previousSpell,spell.Right)<.006f,"spell reach and recovery remain smooth at block boundaries");
            previousSpell=spell.Right;
            casting=spell.Cast>.25f;
            if(spell.Cast>.25f)Check(spell.RightRoll>110f,"visible spell uses an upward-facing palm");
        }
        Check(large&&small,"varied restrained spell strengths exist");
        Check(longestMotionlessAir<6,"merchant never parks a pinched coin in midair");
        Check(experiments==3&&quietSeconds>110f,"shared schedule leaves long quiet reading intervals between experiments");
        Check(Mathf.Abs(strengths[0]-strengths[1])>.05f&&Mathf.Abs(strengths[1]-strengths[2])>.10f,"spell experiment strength varies between shared clock blocks");
        Check(modes[0]<.01f&&modes[1]>.49f&&modes[1]<.51f&&modes[2]>.99f,
            "shared clock selects three distinct spell effect modes");
        Check(Vector3.Distance(spellPeaks[0],spellPeaks[1])>.04f
            &&Vector3.Distance(spellPeaks[1],spellPeaks[2])>.08f,
            "spell phrases change hand choreography as well as light effects");
        var prayer=new TownActivityPose{WorkClock=4f,TransitionAge=TownServiceActivityMotion.TransitionSeconds};
        TownServiceActivityMotion.Engage(ref prayer,true);
        prayer=TownServiceActivityMotion.Advance(prayer,TownServiceActivityMotion.TransitionSeconds);
        var receiving=TownServiceActivityMotion.Visual(2,in prayer);
        Check(receiving.Left.x>.23f&&receiving.Right.x<-.23f
            &&receiving.Left.y<1.10f&&receiving.Right.y<1.10f
            &&receiving.Left.z<.45f&&receiving.Right.z<.45f,
            "attentive priestess lowers both hands beside her robe and clears the bowl");
        var pause=new TownActivityPose{WorkClock=1.8f,TransitionAge=TownServiceActivityMotion.TransitionSeconds};
        TownServiceActivityMotion.Engage(ref pause,true);pause=TownServiceActivityMotion.Advance(pause,1f);
        var held=TownServiceActivityMotion.Visual(1,in pause);
        Check(held.CoinGrip.x==1f,"visitor interruption preserves held coin contact");
        Check(held.RightRoll<90f&&held.Left.y<1.10f&&held.Left.x>.27f
            &&held.Right.x<-.27f&&held.Right.z<.26f,
            "visitor attention settles merchant with hands at his hips without an unsolicited offering");
        TownServiceActivityMotion.ApplyMerchantOffering(ref held,1f);
        Check(held.RightRoll>170f&&held.Right.y>1.17f,
            "a held or parked card independently opens the merchant offering palm");
        var early=new TownActivityPose{WorkClock=1.4f,TransitionAge=TownServiceActivityMotion.TransitionSeconds};
        var late=new TownActivityPose{WorkClock=2.4f,TransitionAge=TownServiceActivityMotion.TransitionSeconds};
        var firstReach=TownServiceActivityMotion.Visual(1,in early);
        var secondReach=TownServiceActivityMotion.Visual(1,in late);
        Check(Vector3.Distance(firstReach.Right,secondReach.Right)>.005f,
            "merchant support hand participates in each transfer");
        for(float entry=0f;entry<TownServiceActivityMotion.MerchantCycleSeconds;entry+=.73f)
        {
            var settling=new TownActivityPose{WorkClock=entry,TransitionAge=TownServiceActivityMotion.TransitionSeconds};
            int frames=0;
            for(;frames<360&&!settling.Engaged;frames++)
            {
                if(TownServiceActivityMotion.MerchantCanAttend(settling.WorkClock))
                    TownServiceActivityMotion.Engage(ref settling,true);
                settling=TownServiceActivityMotion.Advance(settling,1f/90f);
            }
            Check(settling.Engaged&&frames<360,"merchant finishes a held transfer before greeting every arrival phase");
            var settled=TownServiceActivityMotion.Visual(1,in settling);
            Check(Vector3.Distance(settled.CoinGrip,Vector3.zero)==0f,
                "visitor transition begins with each counted coin supported by the counter");
        }
    }
    private static void GeneratedMotion()
    {
        float maximumBodyStep=0,maximumLoopStep=0;
        for(int clip=0;clip<4;clip++)
        {
            var first=TownServiceMotionClips.Sample(clip,0);var last=TownServiceMotionClips.Sample(clip,1);
            Check(Vector3.Distance(first.Left,last.Left)<.00001f,"generated hand loop closes");
            Check(Vector3.Distance(first.Body.Offset,last.Body.Offset)<.00001f,"generated root loop closes");
            float torsoTravel=0;var previous=first;
            int frames=TownServiceMotionClips.Frames(clip)*3;
            for(int frame=1;frame<=frames;frame++)
            {
                var current=TownServiceMotionClips.Sample(clip,(float)frame/frames);
                for(int bone=0;bone<12;bone++)
                {
                    float step=Quaternion.Angle(previous.Body.Get(bone),current.Body.Get(bone));
                    maximumBodyStep=Mathf.Max(maximumBodyStep,step);
                    Check(step<4f,"generated body interpolation has no frame discontinuity");
                    if(frame==frames)maximumLoopStep=Mathf.Max(maximumLoopStep,step);
                }
                torsoTravel=Mathf.Max(torsoTravel,Quaternion.Angle(first.Body.Chest,current.Body.Chest));
                previous=current;
            }
            Check(torsoTravel>.5f,"generated occupation contains real torso movement");
        }
        Console.WriteLine("Motion metrics: maximum body step="+maximumBodyStep+" degrees, final seam step="+maximumLoopStep+" degrees at 90 Hz");
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
                    Transform leftHand=root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Hand.L");
                    Quaternion previousLeft=Quaternion.identity;float maxLeftStep=0f;
                    Transform upper=root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="UpperArm.R");
                    Transform fore=root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Forearm.R");
                    Transform[] thumbs=root.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("Thumb")).ToArray();
                    Quaternion[] thumbNeutral=thumbs.Select(t=>t.localRotation).ToArray();
                    var phase=new TownActivityPose{TransitionAge=TownServiceActivityMotion.TransitionSeconds};Quaternion previousHand=Quaternion.identity;
                    Transform[] feet=root.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="Foot.L"||t.name=="Foot.R").ToArray();
                    Check(feet.Length==2,"both planted feet exist");float maxFootDrift=0,maxPalmError=0,maxHandStep=0;
                    var twistSupports=root.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("ForearmTwist")).ToArray();
                    var previousSupports=new Quaternion[twistSupports.Length];float maxSupportStep=0f;
                    var sampledFeet=new Vector3[2];
                    var previousKnees=new Vector3[2]; float maximumKneeStep=0;
                    for(int n=0;n<5600;n++)
                    {
                        if(n==833)grounding.Apply(.035f,-.02f);if(n==1666)grounding.Apply(-.035f,-.02f);
                        rig.BeforeBodySample();animation.Stop();var body=animation["Idle"];body.enabled=true;body.weight=1;body.time=n/90f;animation.Sample();body.enabled=false;
                        Quaternion before=upper.localRotation;for(int foot=0;foot<2;foot++)sampledFeet[foot]=feet[foot].position;
                        if(n==500)TownServiceActivityMotion.Engage(ref phase,true);if(n==1100)TownServiceActivityMotion.Engage(ref phase,false);
                        phase=TownServiceActivityMotion.Advance(phase,1f/90f);
                        TownServiceActivityMotion.Hands(service,in phase,out _,out var target,out _);
                        float reach=Vector3.Distance(upper.position,fore.position)+Vector3.Distance(fore.position,hand.position);
                        Vector3 wanted=root.TransformPoint(target);float excess=Mathf.Max(0,Vector3.Distance(wanted,upper.position)-reach+.001f);
                        rig.Apply(in phase);
                        var actualVisual=TownServiceActivityMotion.Visual(service,in phase);
                        if(service==3&&actualVisual.Cast>.25f&&actualVisual.RightRoll>=165f)
                            Check(rig.OfferingPalm!=null&&Vector3.Dot(rig.OfferingPalm.up,root.up)>.85f,"actual casting palm supports the spell from below frame="+n+" clock="+phase.WorkClock+" dot="+(rig.OfferingPalm==null?0f:Vector3.Dot(rig.OfferingPalm.up,root.up)));
                        foreach(string side in new[]{"L","R"})
                        {
                            Transform[] joints=root.GetComponentsInChildren<Transform>(true);
                            Transform wrist=joints.Single(t=>t.name=="Hand."+side), elbow=joints.Single(t=>t.name=="Forearm."+side);
                            Transform palmAxis=joints.Single(t=>t.name=="PalmContact."+side);
                            if(service == 3 && phase.TransitionAge >= TownServiceActivityMotion.TransitionSeconds && !phase.Engaged && actualVisual.Cast < .001f
                                && Mathf.Abs(actualVisual.RightRoll - 65f) < .01f)
                                Check(Vector3.Dot(palmAxis.forward, root.right) * (side == "L" ? -1f : 1f) > .60f,
                                    "relaxed enchantress palms face inward symmetrically: " + side);
                            if(service==3&&TownServiceActivityMotion.Blend(in phase)<.001f)
                            {
                                Transform shoulderJoint=joints.Single(t=>t.name=="UpperArm."+side);
                                Check(Vector3.Dot(palmAxis.position-shoulderJoint.position,root.up)/root.lossyScale.x<-.005f,"spell shaping palm stays below its shoulder");
                                Check(Vector3.Dot(elbow.position-shoulderJoint.position,root.up)/root.lossyScale.x<-.07f,"spell elbow remains relaxed below the shoulder");
                            }
                            Check(Vector3.Angle(wrist.position-elbow.position,palmAxis.up)<55.1f,"actual wrist flexion remains anatomical: "+npc+" "+side+" frame="+n+" angle="+Vector3.Angle(wrist.position-elbow.position,palmAxis.up)+" wrist="+root.InverseTransformPoint(wrist.position)+" elbow="+root.InverseTransformPoint(elbow.position));
                            int supportCount=joints.Count(t=>t.name.StartsWith("ForearmTwist")&&t.name.EndsWith("."+side));
                            // The priestess FBX predates the three-support hand rig;
                            // its source has no twist bones and uses the solver fallback.
                            Check(supportCount==(service==2?0:3),"imported pronation has the source-authored skin support count: "+npc+" "+side+" found="+supportCount);
                        }
                        for (int leg=0;leg<2;leg++)
                        {
                            string side=leg==0?"L":"R";
                            Transform thigh=root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Thigh."+side);
                            Transform shin=root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Shin."+side);
                            Vector3 axis=(feet[leg].position-thigh.position).normalized;
                            Vector3 bend=Vector3.ProjectOnPlane(shin.position-thigh.position,axis).normalized;
                            Vector3 forward=Vector3.ProjectOnPlane(-root.forward,axis).normalized;
                            Check(Vector3.Dot(bend,forward)>.999f,"planted knee keeps anatomical forward bend plane: "+npc+" frame="+n);
                            if(n>0&&n!=833&&n!=1666)maximumKneeStep=Mathf.Max(maximumKneeStep,Vector3.Distance(previousKnees[leg],shin.position));
                            previousKnees[leg]=shin.position;
                        }
                        if(service==2&&TownServiceActivityMotion.Blend(in phase)<.001f)
                        {
                            var contacts=root.GetComponentsInChildren<Transform>(true);
                            Transform lp=contacts.Single(t=>t.name=="PalmContact.L"),rp=contacts.Single(t=>t.name=="PalmContact.R");
                            Check(Vector3.Distance(lp.position,rp.position)/root.lossyScale.x<.03f,"prayer joins cupped hands at the sternum");
                            Check(Mathf.Abs(root.InverseTransformPoint(lp.position).y-actualVisual.Left.y)<.01f && actualVisual.Left.y>=1.23f && actualVisual.Left.y<=1.34f,"prayer hands stay below the face");
                        }
                        for(int foot=0;foot<2;foot++)maxFootDrift=Mathf.Max(maxFootDrift,Vector3.Distance(feet[foot].position,sampledFeet[foot]));
                        // These two fixture frames teleport the actor to another ground
                        // sample. Anatomical hand direction follows the changed forearm;
                        // only continuous work/attention frames have a velocity bound.
                        bool continuous = n>0&&n!=833&&n!=1666;
                        if(continuous)maxHandStep=Mathf.Max(maxHandStep,Quaternion.Angle(previousHand,hand.rotation));
                        if(continuous)
                        {
                            float leftStep=Quaternion.Angle(previousLeft,leftHand.rotation);maxLeftStep=Mathf.Max(maxLeftStep,leftStep);
                            Check(leftStep<5f,"actual left hand remains continuous: "+npc+" frame="+n+" step="+leftStep);
                        }
                        previousLeft=leftHand.rotation;
                        for(int support=0;support<twistSupports.Length;support++)
                        {
                            if(continuous)
                            {
                                float step=Quaternion.Angle(previousSupports[support],twistSupports[support].rotation);
                                maxSupportStep=Mathf.Max(maxSupportStep,step);
                                Check(step<8f,"actual forearm skin support stays continuous across pronation: "+npc+" "+twistSupports[support].name+" frame="+n+" step="+step);
                            }
                            previousSupports[support]=twistSupports[support].rotation;
                        }
                        for(int digit=0;digit<thumbs.Length;digit++)
                            Check(Quaternion.Angle(thumbs[digit].localRotation,thumbNeutral[digit])<55f,"anatomical thumb stays inside natural grasp range");
                        if(service==1&&TownServiceActivityMotion.Writing(phase.WorkClock)>.99f&&TownServiceActivityMotion.Blend(in phase)<.01f)
                            Check(Vector3.Distance(hand.position,wanted)<.003f,"writing contact survives resolved terrain offsets at "+n+": "+Vector3.Distance(hand.position,wanted));
                        // A smooth offered-palm turn over the authored transition has a bounded
                        // peak at 90Hz; the lower hands give the prayer recovery a comparable arc.
                        float blend=TownServiceActivityMotion.Blend(in phase);
                        float turnLimit=blend>0f&&blend<1f?5f:4f;
                        if(continuous)Check(Quaternion.Angle(previousHand,hand.rotation)<turnLimit,"hand orientation remains smooth through prayer interruption: "+npc+" n="+n+" step="+Quaternion.Angle(previousHand,hand.rotation));
                        previousHand=hand.rotation;
                        if(TownServiceActivityMotion.Blend(in phase)<.001f && target.y>.99f)
                        {
                            Transform actualPalm=root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="PalmContact.R");
                            maxPalmError=Mathf.Max(maxPalmError,Vector3.Distance(actualPalm.position,wanted));
                            Check(Vector3.Distance(actualPalm.position,wanted)<.012f,"actual palm reaches occupation target: "+npc+" n="+n+" error="+Vector3.Distance(actualPalm.position,wanted));
                        }
                        else if(TownServiceActivityMotion.Blend(in phase)>.999f)
                        {
                            Transform[] markers=root.GetComponentsInChildren<Transform>(true);
                            Transform palm=markers.Single(t=>t.name=="PalmContact.R");
                            Transform[] supports=markers.Where(t=>t.name=="PalmContact.R"||t.name.EndsWith("Pad.R")).ToArray();
                            Check(supports.Length==6,"actual hand has five anatomical finger pads and palm support");
                            float lowest=supports.Min(t=>root.InverseTransformPoint(t.position).y);
                            if(service!=2)
                            {
                                Check(Vector3.Distance(palm.position,wanted)<.02f,"actual enchantress offered palm reaches handoff n="+n+" distance="+Vector3.Distance(palm.position,wanted)+" shoulder="+upper.position+" target="+wanted+" reach="+reach);
                                // Merchant attention is not an offering. The right palm
                                // points up only when a card is separately offered.
                                if(service==3)
                                    Check(rig.OfferingPalm!=null&&Vector3.Dot(rig.OfferingPalm.up,root.up)>.99f,
                                        "actual offering normal points above palm: "+npc+" frame="+n
                                        +" dot="+(rig.OfferingPalm==null?0f:Vector3.Dot(rig.OfferingPalm.up,root.up)));
                            }
                            else Check(Vector3.Distance(palm.position,wanted)<.09f
                                && Mathf.Abs(root.InverseTransformPoint(palm.position).x)>.22f,
                                "attentive priest lowers hands beside her robe and away from the bowl: frame="+n
                                +" error="+Vector3.Distance(palm.position,wanted)+" x="+root.InverseTransformPoint(palm.position).x);
                            if(service!=2)
                                Check(supports.All(t=>root.InverseTransformPoint(t.position).y>=.954f),"actual palmar skin stays above wood");
                        }
                        rig.BeforeBodySample();Check(Quaternion.Angle(upper.localRotation,before)<.05f,"original arm base restores without accumulation");
                    }
                    Console.WriteLine("Actual motion metrics "+npc+": foot drift="+maxFootDrift+"m palm error="+maxPalmError+"m hand step="+maxHandStep+" degrees/90Hz frame");
                    Console.WriteLine("Maximum knee step "+npc+": "+maximumKneeStep+"m/90Hz frame");
                    Check(maximumKneeStep<.006f,"planted knees do not twitch while the body changes weight: "+npc+" "+maximumKneeStep);
                    Check(maxFootDrift<.003f,"generated stance keeps actual imported feet planted: "+npc+" "+maxFootDrift);
                    Console.WriteLine("Actual additional continuity metrics "+npc+": left hand="+maxLeftStep+" support="+maxSupportStep+" degrees/90Hz frame");
                    grounding.Apply(0f,0f);ArmGeometry.Export(root, service, rig, animation);ActivityRender.Render(obj, service, rig);
                }
                finally{UnityEngine.Object.DestroyImmediate(obj);}
            }
        }
        finally{if(bundle!=null)bundle.Unload(true);}
    }
}
