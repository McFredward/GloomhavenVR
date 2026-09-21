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
                    Transform fore=Child(upper,"Forearm."+side,new Vector3(sign*.04f,-.20f,-.10f));
                    Transform hand=Child(fore,"Hand."+side,new Vector3(0f,-.12f,-.20f));
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
                    Transform contact=root.GetComponentsInChildren<Transform>().Single(t=>t.name=="PalmContact."+side);
                    Vector3 expected=root.TransformPoint(side=="L"?visual.Left:visual.Right);
                    Vector3 horizontal=contact.position-expected;horizontal=Vector3.ProjectOnPlane(horizontal,root.up);
                    if(horizontal.magnitude>.0001f)throw new Exception("anatomical palm contacts transformed counter surface");checks++;
                    float lowest=root.GetComponentsInChildren<Transform>().Where(t=>t.name=="PalmContact."+side||t.name.EndsWith("Pad."+side)).Min(t=>root.InverseTransformPoint(t.position).y);
                    if(Mathf.Abs(lowest-.959f)>.0001f)throw new Exception("attentive palms rest at physical worktop height");checks++;
                    foreach(Transform tip in root.GetComponentsInChildren<Transform>().Where(t=>t.name.EndsWith("Tip."+side)))
                    {if(Quaternion.Angle(tip.localRotation,Quaternion.identity)>.001f)throw new Exception("contact markers are not articulated finger joints");checks++;}
                }
                if(service==1)
                {
                    using var props=new TownServiceActivityProps(root,service,Shader.Find("Unlit/Color"));
                    var coin=Child(root,"Native coin",new Vector3(-.2f,.96f,.22f));props.BindCoin(coin,Vector3.zero);props.Sample(in visual);
                    if(Vector3.Distance(coin.localPosition,new Vector3(-.2f,.96f,.22f))>.0001f)throw new Exception("attentive merchant sets coin down");checks++;
                    Transform pen=root.Find("Town.ReedPen");
                    if(Mathf.Abs(pen.localPosition.y-.966f)>.0001f)throw new Exception("attentive merchant sets pen onto ledger edge");checks++;
                }
                rig.BeforeBodySample();
                state=new TownActivityPose{WorkClock=8f,TransitionAge=.65f};
                visual=TownServiceActivityMotion.Visual(service,in state);rig.Apply(in visual);
                if(Quaternion.Angle(chest.localRotation,Quaternion.identity)>8.05f || Quaternion.Angle(neck.localRotation,Quaternion.identity)>4.05f)
                    throw new Exception("work posture does not stack an extreme torso and neck bow");checks++;
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
