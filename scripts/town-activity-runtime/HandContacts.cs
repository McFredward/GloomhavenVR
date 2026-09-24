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
                var rig=new TownServiceActivityRig(root,service);
                var state=new TownActivityPose{Engaged=true,FromBlend=1f,TransitionAge=.65f};
                var visual=TownServiceActivityMotion.Visual(service,in state);
                rig.Apply(in visual);
                foreach(string side in new[]{"L","R"})
                {
                    Transform contact=service==1&&side=="L"?root.Find("ActivityGripLeft"):root.GetComponentsInChildren<Transform>().Single(t=>t.name=="PalmContact."+side);
                    Vector3 expected=root.TransformPoint(side=="L"?visual.Left:visual.Right);
                    Vector3 horizontal=contact.position-expected;horizontal=Vector3.ProjectOnPlane(horizontal,root.up);
                    if(horizontal.magnitude>.0001f)throw new Exception("anatomical palm contacts transformed counter surface service="+service+" side="+side+" error="+horizontal.magnitude);checks++;
                    float lowest=root.GetComponentsInChildren<Transform>().Where(t=>t.name=="PalmContact."+side||t.name.EndsWith("Pad."+side)).Min(t=>root.InverseTransformPoint(t.position).y);
                    if(!(service==1&&side=="L")&&!(service!=2&&side=="R"))
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
                        var work=new TownActivityPose{WorkClock=clock,TransitionAge=.65f};
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
                rig.BeforeBodySample();
                state=new TownActivityPose{WorkClock=8f,TransitionAge=.65f};
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
