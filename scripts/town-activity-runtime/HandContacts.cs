using System;
using System.Linq;
using GloomhavenVR.Net;
using GloomhavenVR.WorldUI;
using UnityEngine;

internal static class HandContacts
{
    private static Transform Child(Transform parent,string name,Vector3 position)
    {var obj=new GameObject(name);obj.transform.SetParent(parent,false);obj.transform.localPosition=position;return obj.transform;}
    internal static int Run()
    {
        int checks=0;
        foreach(byte service in new byte[]{1,2,3})
        {
            var root=new GameObject("Anatomical contact fixture").transform;
            root.position=new Vector3(3,2,-1);root.rotation=Quaternion.Euler(0,41,0);root.localScale=Vector3.one*.7f;
            try
            {
                foreach(string side in new[]{"L","R"})
                {
                    float sign=side=="L"?1f:-1f;
                    Transform upper=Child(root,"UpperArm."+side,new Vector3(sign*.22f,1.35f,.60f));
                    Transform fore=Child(upper,"Forearm."+side,new Vector3(sign*.07f,-.24f,-.13f));
                    Transform hand=Child(fore,"Hand."+side,new Vector3(0f,-.14f,-.22f));
                    foreach(string digit in new[]{"Thumb","Index","Middle","Ring","Little"})
                    {
                        Transform finger=Child(hand,digit+"1."+side,new Vector3(.01f,-.002f,-.055f));
                        finger=Child(finger,digit+"2."+side,new Vector3(0f,.023f,0f));
                        finger=Child(finger,digit+"3."+side,new Vector3(0f,.020f,0f));
                        Child(finger,digit+"Tip."+side,new Vector3(0f,.012f,0f));
                        Transform pad=Child(finger,digit+"Pad."+side,Vector3.zero);
                        pad.position=hand.TransformPoint(new Vector3(.01f,-.030f,-.12f));
                    }
                    Transform contact=Child(hand,"PalmContact."+side,new Vector3(0f,-.018f,-.070f));
                    contact.localRotation=Quaternion.LookRotation(Vector3.down,Vector3.back);
                }
                Transform chest=Child(root,"Chest",Vector3.zero);
                Transform neck=Child(root,"Neck",Vector3.zero);
                Transform body=Child(root,"LOD0_Costume",Vector3.zero);
                body.gameObject.AddComponent<SkinnedMeshRenderer>().sharedMaterial =
                    new Material(Shader.Find("Unlit/Color"));
                var rig=new TownServiceActivityRig(root,service);
                using var sleeves=new TownServiceSleeveLining(root,root,service);
                var state=new TownActivityPose{Engaged=true,FromBlend=1f,TransitionAge=TownServiceActivityMotion.TransitionSeconds};
                var visual=TownServiceActivityMotion.Visual(service,in state);
                if(service==1)
                {
                    if(visual.RightRoll>90f)throw new Exception("gaze alone does not open merchant offering palm");checks++;
                    TownServiceActivityMotion.ApplyMerchantOffering(ref visual,1f);
                }
                rig.Apply(in visual);
                sleeves.Tick();
                foreach(string side in new[]{"L","R"})
                {
                    Transform hem=root.Find("SewnSleeveInterior."+side);
                    if(hem==null)throw new Exception("inner cuff is attached to both moving arms");checks++;
                    Mesh sewn=hem.GetComponent<MeshFilter>().sharedMesh;
                    if(!sewn.triangles.Contains(97))
                        throw new Exception("shallow cuff diaphragm hides the severed forearm end");checks++;
                    Transform wrist=root.GetComponentsInChildren<Transform>().Single(t=>t.name=="Hand."+side);
                    Transform forearm=root.GetComponentsInChildren<Transform>().Single(t=>t.name=="Forearm."+side);
                    Vector3 axis=(wrist.position-forearm.position).normalized;
                    float capDepth=Vector3.Dot(hem.TransformPoint(sewn.vertices[97])-wrist.position,axis);
                    if(capDepth<-.021f||capDepth>-.017f)
                        throw new Exception("shallow cuff diaphragm hides the severed forearm end");checks++;
                    for(int vertex=0;vertex<24;vertex++)
                    {
                        Vector3 delta=hem.TransformPoint(sewn.vertices[vertex])-wrist.position;
                        float depth=Vector3.Dot(delta,axis);
                        float radius=Vector3.ProjectOnPlane(delta,axis).magnitude;
                        if(depth<-.016f||depth>-.008f||radius<.023f||radius>.033f)
                            throw new Exception("inner cuff overlaps the anatomical wrist ahead of the cut");
                        checks++;
                    }
                    // A released attentive merchant is authored by his anatomical palm.
                    // ActivityGripLeft exists for an actually pinched work coin and is offset
                    // from that surface; treating it as the hip contact preserved the former
                    // table-reaching pose in the test after the coin had already been released.
                    Transform contact=service==1&&side=="L"&&visual.CoinGrip.x>.5f
                        ?root.Find("ActivityGripLeft")
                        :root.GetComponentsInChildren<Transform>().Single(t=>t.name=="PalmContact."+side);
                    Vector3 expected=root.TransformPoint(side=="L"?visual.Left:visual.Right);
                    Vector3 horizontal=contact.position-expected;horizontal=Vector3.ProjectOnPlane(horizontal,root.up);
                    // The attentive priestess target is an unconstrained relaxed pose beside the
                    // robe, not a physical contact point. Her short imported arms retain their
                    // joint limit instead of stretching to the authored guide.
                    // Hip rests are unconstrained poses, not prop contacts. Both imported
                    // torsos have shorter arms than the generated guide; preserve their
                    // joint limit and verify the resulting anatomical region below.
                    float contactTolerance=service==2?.095f:service==1?.10f:.0001f;
                    if(horizontal.magnitude>contactTolerance)throw new Exception("anatomical palm contacts transformed counter surface service="+service+" side="+side+" error="+horizontal.magnitude);checks++;
                    float lowest=root.GetComponentsInChildren<Transform>().Where(t=>t.name=="PalmContact."+side||t.name.EndsWith("Pad."+side)).Min(t=>root.InverseTransformPoint(t.position).y);
                    if(service==2)
                    {
                        Vector3 lowered=root.InverseTransformPoint(contact.position);
                        // The imported upper/forearm lengths end above the .74 guide;
                        // verify the solved anatomical palm, not the unreachable guide.
                        // It must hang below the .955 worktop and beside the robe.
                        if(Mathf.Abs(lowered.x)<.12f||lowered.z<.40f||lowered.z>.59f
                            ||lowered.y<.96f||lowered.y>1.12f)
                            throw new Exception("attentive priestess hands stay beside her robe and outside the donation bowl: "+side+" "+lowered);
                        Vector3 inward=(side=="L"?-root.right:root.right);
                        if(Vector3.Dot(contact.forward,inward)<.55f)
                            throw new Exception("attentive priestess palms rest naturally against her hips: "+side);
                        checks++;
                    }
                    else if(service==1&&side=="L")
                    {
                        Vector3 hip=root.InverseTransformPoint(contact.position);
                        if(Mathf.Abs(hip.x)<.08f||hip.z<.42f||hip.z>.59f
                            ||hip.y<.88f||hip.y>1.04f)
                            throw new Exception("attentive merchant free hand rests on his hip behind the counter: "+side+" "+hip);
                        Vector3 inward=side=="L"?-root.right:root.right;
                        if(Vector3.Dot(contact.forward,inward)<.55f)
                            throw new Exception("attentive merchant palm rests naturally against his hip: "+side);
                        checks++;
                    }
                    else if(!(service==1&&side=="L")&&!(service!=2&&side=="R"))
                    {if(lowest<1.02f)throw new Exception("attentive palms remain clear of the worktop");checks++;}
                    if(service!=2&&side=="R")
                    {if(rig.OfferingPalm==null||Vector3.Dot(rig.OfferingPalm.up,root.up)<.99f)throw new Exception("offering palm faces upward");checks++;}
                    foreach(Transform tip in root.GetComponentsInChildren<Transform>().Where(t=>t.name.EndsWith("Tip."+side)))
                    {if(Quaternion.Angle(tip.localRotation,Quaternion.identity)>.001f)throw new Exception("contact markers are not articulated finger joints");checks++;}
                }
                if(service==1)
                {
                    using var props=new TownServiceActivityProps(root,service,Shader.Find("Unlit/Color"));
                    var coin=Child(root,"Native coin",new Vector3(-.2f,.96f,.22f));props.BindCoin(coin,Vector3.zero);props.Sample(in visual);
                    if(Vector3.Distance(coin.localPosition,visual.Coin0)>.0001f)throw new Exception("ungripped coin remains on its real seat");checks++;
                    if(root.Find("Town.ReedPen")!=null)throw new Exception("imaginary writing prop removed");checks++;
                    foreach(float clock in new[]{.5f,.8f,1f,1.3f,1.8f,2.84f,3.2f,3.6f,5.2f,9.5f,20.99f})
                    {
                        rig.BeforeBodySample();
                        var work=new TownActivityPose{WorkClock=clock,TransitionAge=TownServiceActivityMotion.TransitionSeconds};
                        var sample=TownServiceActivityMotion.Visual(1,in work);rig.Apply(in sample);props.Sample(in sample);
                        // The settled phase8 has zero merchant grasp. Sample the actual
                        // pinching interval too, or an excessive curl factor can escape.
                        foreach(Transform thumb in root.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("Thumb")&&!t.name.Contains("Tip")&&!t.name.Contains("Pad")))
                        {if(Quaternion.Angle(thumb.localRotation,Quaternion.identity)>55f)throw new Exception("anatomical thumb stays inside natural grasp range");checks++;}
                        for(int i=0;i<3;i++)
                        {
                            Transform shown=i==0?coin:root.Find("Town.CountingCoin"+i);
                            float attached=i==0?sample.CoinGrip.x:i==1?sample.CoinGrip.y:sample.CoinGrip.z;
                            if(attached!=0f&&attached!=1f)throw new Exception("coin is resting or rigidly gripped, never magnetically attracted");checks++;
                            Vector3 resting=i==0?sample.Coin0:i==1?sample.Coin1:sample.Coin2;
                            Vector3 wanted=attached>.99f?root.Find("ActivityGripLeft").position:root.TransformPoint(resting);
                            if(Vector3.Distance(shown.position,wanted)>.0001f)throw new Exception("real coin follows actual pinch or resting seat");checks++;
                        }
                    }
                }
                if(service==2)
                {
                    Transform leftPalm=root.GetComponentsInChildren<Transform>().Single(t=>t.name=="PalmContact.L");
                    Transform rightPalm=root.GetComponentsInChildren<Transform>().Single(t=>t.name=="PalmContact.R");
                    Quaternion previousLeft=leftPalm.rotation,previousRight=rightPalm.rotation;
                    for(int frame=1;frame<=64;frame++)
                    {
                        rig.BeforeBodySample();
                        var covered=visual;
                        TownServiceActivityMotion.ApplyTempleAvailability(ref covered,false,frame/64f);
                        rig.Apply(in covered);
                        float leftStep=Quaternion.Angle(previousLeft,leftPalm.rotation),rightStep=Quaternion.Angle(previousRight,rightPalm.rotation);
                        if(leftStep>5f||rightStep>5f)
                            throw new Exception("unavailable temple pose transitions continuously without a wrist snap frame="+frame+" left="+leftStep+" right="+rightStep);
                        previousLeft=leftPalm.rotation;previousRight=rightPalm.rotation;
                    }
                    Vector3 left=root.InverseTransformPoint(leftPalm.position),right=root.InverseTransformPoint(rightPalm.position);
                    if(Mathf.Abs(left.x)>.10f||Mathf.Abs(right.x)>.10f||left.y<1.05f||right.y<1.08f
                        ||left.y>1.14f||right.y>1.14f)
                        throw new Exception("unavailable donation places both hands over the shared bowl left="+left+" right="+right);
                    if(Vector3.Dot(leftPalm.forward,-root.up)<.72f||Vector3.Dot(rightPalm.forward,-root.up)<.72f)
                        throw new Exception("unavailable donation covers the bowl with both palms facing down");
                    for(int frame=63;frame>=0;frame--)
                    {
                        rig.BeforeBodySample();
                        var recovering=visual;
                        TownServiceActivityMotion.ApplyTempleAvailability(ref recovering,true,frame/64f);
                        rig.Apply(in recovering);
                        float leftStep=Quaternion.Angle(previousLeft,leftPalm.rotation),rightStep=Quaternion.Angle(previousRight,rightPalm.rotation);
                        if(leftStep>5f||rightStep>5f)
                            throw new Exception("available temple returns continuously without dropping the cover pose frame="+frame+" left="+leftStep+" right="+rightStep);
                        previousLeft=leftPalm.rotation;previousRight=rightPalm.rotation;
                    }
                    // Reproduce the reported path directly: donation stays unavailable
                    // while the visitor walks away. Attention and the cover contribution
                    // must decay on the same continuous curve into the prayer pose.
                    var departure=new TownActivityPose{WorkClock=4f,FromBlend=1f,Engaged=true,
                        TransitionAge=TownServiceActivityMotion.TransitionSeconds};
                    var departing=TownServiceActivityMotion.Visual(2,in departure);
                    TownServiceActivityMotion.ApplyTempleAvailability(ref departing,false,1f);
                    rig.BeforeBodySample();rig.Apply(in departing);
                    previousLeft=leftPalm.rotation;previousRight=rightPalm.rotation;
                    Vector3 previousLeftPosition=leftPalm.position,previousRightPosition=rightPalm.position;
                    TownServiceActivityMotion.Engage(ref departure,false);
                    for(int frame=1;frame<=70;frame++)
                    {
                        departure=TownServiceActivityMotion.Advance(departure,1f/90f);
                        departing=TownServiceActivityMotion.Visual(2,in departure);
                        TownServiceActivityMotion.ApplyTempleAvailability(ref departing,false,1f);
                        rig.BeforeBodySample();rig.Apply(in departing);
                        float leftStep=Quaternion.Angle(previousLeft,leftPalm.rotation),rightStep=Quaternion.Angle(previousRight,rightPalm.rotation);
                        float leftTravel=Vector3.Distance(previousLeftPosition,leftPalm.position),rightTravel=Vector3.Distance(previousRightPosition,rightPalm.position);
                        if(leftStep>5f||rightStep>5f||leftTravel>.015f||rightTravel>.015f)
                            throw new Exception("unavailable temple attention exit remains continuous frame="+frame
                                +" leftDegrees="+leftStep+" rightDegrees="+rightStep
                                +" leftTravel="+leftTravel+" rightTravel="+rightTravel);
                        previousLeft=leftPalm.rotation;previousRight=rightPalm.rotation;
                        previousLeftPosition=leftPalm.position;previousRightPosition=rightPalm.position;
                    }
                    checks+=201;
                }
                rig.BeforeBodySample();
                state=new TownActivityPose{WorkClock=8f,TransitionAge=TownServiceActivityMotion.TransitionSeconds};
                visual=TownServiceActivityMotion.Visual(service,in state);rig.Apply(in visual);
                if(Quaternion.Angle(chest.localRotation,Quaternion.identity)>13f || Quaternion.Angle(neck.localRotation,Quaternion.identity)>4.05f)
                    throw new Exception("work posture does not stack an extreme torso and neck bow service="+service+" chest="+Quaternion.Angle(chest.localRotation,Quaternion.identity)+" neck="+Quaternion.Angle(neck.localRotation,Quaternion.identity));checks++;
                foreach(Transform child in root.GetComponentsInChildren<Transform>())
                {
                    if((child.name.Contains("Tip.")||child.name.Contains("Pad."))&&Quaternion.Angle(child.localRotation,Quaternion.identity)>.001f)
                        throw new Exception("contact markers are not articulated finger joints");
                    if(child.name.StartsWith("Thumb")&&!child.name.Contains("Tip")&&!child.name.Contains("Pad")&&Quaternion.Angle(child.localRotation,Quaternion.identity)>55f)
                        throw new Exception("anatomical thumb stays inside natural grasp range");
                }
                checks+=2;
                rig.Suspend();
            }
            finally{UnityEngine.Object.DestroyImmediate(root.gameObject);}
        }
        return checks;
    }
}
