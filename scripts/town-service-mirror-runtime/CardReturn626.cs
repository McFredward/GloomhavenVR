using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using UnityEngine;

public static partial class MirrorProgram
{
    private static IEnumerator CardReturns626()
    {
        TownServiceMirror.Shutdown(); NetPlayerActors.Peer=1;
        Transform shared=Go("Card return actual shared frame").transform;
        shared.rotation=Quaternion.Euler(0f,19f,0f);
        Transform observer=Go("Observer of original return").transform;
        observer.SetPositionAndRotation(new Vector3(9f,0f,0f),shared.rotation);
        TownServiceMirror.SharedFrameForRemote=_=>observer;
        Transform mount=Go("Actual stock moving root",shared).transform;
        mount.localPosition=new Vector3(.4f,1.2f,-.3f);
        mount.localRotation=Quaternion.Euler(21f,37f,-11f);
        Transform face=Source(mount); face.localRotation=Quaternion.Euler(6f,-9f,13f);
        face.localPosition=new Vector3(.025f,-.033f,.009f);
        Func<Transform,bool> boundary=node=>node==face;
        TownServiceMirror.RegisterTemplate(1,1,mount,boundary,address:"merchant.heldstock|");
        TownServiceMirror.RegisterTemplate(1,2,face,address:"item.611|");
        var token=new GloomhavenVR.WorldUI.TownServiceToken();
        using(TownServiceMirror.UseStockLane())
        {
            TownServiceMirror.BeginSession(1,991,shared,shared);
            TownServiceMirror.RegisterModule(17,1,mount,boundary,address:"merchant.heldstock|");
            TownServiceMirror.RegisterModule(10,2,face,address:"item.611|");
            TownServiceMirror.RegisterCardReturn(mount,token.TryCardReturnMotion);
            TownServiceMirror.RegisterCardReturn(face,token.TryCardReturnMotion);
        }
        Canvas.ForceUpdateCanvases();yield return null;
        NetAvatarDriver.PeerHeldStock[2]=611;
        FastCapture warm=CaptureFast();Receive(2,warm.Artwork);DeliverMotion(2,warm);
        TownServiceMirror.TickRemote(_=>observer);
        var keys=(Dictionary<int,int>)typeof(TownServiceMirror).GetField("StockKeys",PrivateStatic)!.GetValue(null)!;
        int stock=keys[2]; TownServiceBinding copy=Remote(stock,10)!, root=Remote(stock,17)!;
        Check(copy!=null&&root!=null,"stock original is fully prepared before its short return begins");
        Check(!root.Root.gameObject.activeInHierarchy,
            "prepared stock original stays hidden while the canonical avatar holds its one card");
        NetAvatarDriver.PeerHeldStock.Remove(2);
        Vector3 from=mount.position; Quaternion fromRotation=mount.rotation; Vector3 size=mount.lossyScale;
        Vector3 target=shared.TransformPoint(new Vector3(-.6f,.9f,.2f));
        Quaternion targetRotation=shared.rotation*Quaternion.Euler(75f,0f,0f);
        Matrix4x4 fromMatrix=Matrix4x4.TRS(from,fromRotation,size),toMatrix=Matrix4x4.TRS(target,targetRotation,size*.72f);
        for(float until=Time.unscaledTime+.075f;Time.unscaledTime<until;)yield return null;
        token.ReturnStarted=Time.unscaledTime-.07f;
        token.ReturnNumbers=TownCardReturnMotion.Capture(face,mount,shared,null,.07f,.35f,0,0f,
            fromMatrix,fromRotation,toMatrix,targetRotation,Vector3.zero);
        token.ReturnSamples[mount]=TownCardReturnMotion.Capture(mount,mount,shared,null,.07f,.35f,0,0f,
            fromMatrix,fromRotation,toMatrix,targetRotation,Vector3.zero);
        FastCapture returned=CaptureFast();Receive(2,returned.Artwork);DeliverMotion(2,returned);
        float received=Time.unscaledTime;TownServiceMotionEntry? clock=null;
        foreach(byte[] bytes in returned.Motion) if(TownServiceMotionCodec.TryRead(bytes,bytes.Length,out var packet))
            foreach(var entry in packet!.Entries)if(entry.Kind==8&&entry.Module==10)clock=entry;
        Check(clock!=null&&clock.Revision==5,"a release publishes the warm original return on its first numeric turn");
        TownServiceMirror.TickRemote(_=>observer);
        Check(root.Root.gameObject.activeInHierarchy,"release exposes the already prepared same stock original immediately");
        float[] data=clock!.Numbers;Vector3 previous=copy.Root.position;
        for(int step=0;step<5;step++)
        {
            yield return null;TownServiceMirror.TickRemote(_=>observer);
            float age=data[0]+Time.unscaledTime-received;
            float t=Mathf.Clamp01(age/data[1]),ease=t*t*(3f-2f*t);
            Matrix4x4 owner=Matrix4x4.TRS(Vector3.Lerp(from,target,ease),Quaternion.Slerp(fromRotation,targetRotation,ease),
                Vector3.Lerp(size,size*.72f,ease));
            Matrix4x4 child=Matrix4x4.TRS(new Vector3(data[28],data[29],data[30]),
                new Quaternion(data[31],data[32],data[33],data[34]),new Vector3(data[35],data[36],data[37]));
            Vector3 actual=observer.TransformPoint(shared.InverseTransformPoint((owner*child).MultiplyPoint3x4(Vector3.zero)));
            Check(Vector3.Distance(copy.Root.position,actual)<.0003f,
                "native card return advances on every rendered frame without another packet actual="+copy.Root.position.ToString("F6")+" expected="+actual.ToString("F6")+" age="+age+" root="+root.Root.position.ToString("F6"));
            Quaternion rotation=observer.rotation*Quaternion.Inverse(shared.rotation)*Quaternion.Slerp(fromRotation,targetRotation,ease)
                *new Quaternion(data[31],data[32],data[33],data[34]);
            Check(Quaternion.Angle(copy.Root.rotation,rotation)<.03f,
                "the exact original card face follows its authored root-relative rotation");
            Check(Vector3.Distance(previous,copy.Root.position)>.00001f,"short native return does not stop between sparse town samples");
            previous=copy.Root.position;
        }
        DeliverMotion(2,warm);TownServiceMirror.TickRemote(_=>observer);
        Check(root.Root.gameObject.activeInHierarchy,"reordered older held metadata cannot revive the retired avatar duplicate");
        NetAvatarDriver.PeerHeldStock.Clear();TownServiceMirror.Shutdown();
    }
}
