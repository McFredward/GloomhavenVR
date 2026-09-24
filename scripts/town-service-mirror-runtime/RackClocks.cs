using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GloomhavenVR.Net.TownServices;
using UnityEngine;
using UnityEngine.UI;

public static partial class MirrorProgram
{
    private static CanvasGroup RemoteGate(int peer,ushort id)=>Remote(peer,id)!.Root.GetComponentInParent<CanvasGroup>();
    private static IEnumerator RackClocks()
    {
        foreach(float scale in new[]{.05f,1f,2f,198.12f})
        {
            TownServiceMirror.Shutdown();TownServiceMirror.ResetNetwork();Baselines.Clear();
            var owner=new GameObject("rack owner").transform;Objects.Add(owner.gameObject);owner.localScale=Vector3.one*scale;
            var observer=new GameObject("rack observer").transform;Objects.Add(observer.gameObject);
            observer.localScale=Vector3.one*scale;observer.rotation=Quaternion.Euler(0,37,0);
            var rack=new GameObject("mechanical rack").transform;rack.SetParent(owner,false);rack.localRotation=Quaternion.Euler(0,15,0);
            var content=new GameObject("cards outside geometry binding").transform;content.SetParent(rack,false);
            var crank=new GameObject("mechanical crank").transform;crank.SetParent(owner,false);crank.localRotation=Quaternion.Euler(0,15,0);
            TownServiceMirror.RegisterTemplate(1,1,rack,child=>child==content,"merchant.rack|");
            TownServiceMirror.RegisterTemplate(1,1,crank,address:"merchant.crank|");
            TownServiceMirror.BeginSession(1,880,owner,rack);
            TownServiceMirror.RegisterModule(1,1,rack,child=>child==content,"merchant.rack|");
            TownServiceMirror.RegisterModule(2,1,crank,address:"merchant.crank|");
            var members=new List<TownRackMember>();var cards=new Dictionary<ushort,Transform>();
            var gates=new Dictionary<ushort,CanvasGroup>();
            for(ushort page=0;page<2;page++)for(int card=0;card<16;card++)for(int part=0;part<3;part++)
            {
                ushort id=(ushort)(3+page*48+card*3+part);
                var gate=new GameObject("page gate",typeof(CanvasGroup)).transform;gate.SetParent(content,false);
                var group=gate.GetComponent<CanvasGroup>();group.alpha=page==0?1f:0f;gates.Add(id,group);
                Transform source;
                string address;
                if(part==1)
                {
                    source=new GameObject("physical body").transform;source.SetParent(gate,false);
                    var mesh=new Mesh{name="clock body"+id};mesh.vertices=new[]{Vector3.zero,Vector3.right*.1f,Vector3.up*.1f};mesh.triangles=new[]{0,1,2};Assets.Add(mesh);
                    source.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
                    var material=new Material(Shader.Find("Unlit/Color")){name="clock material"+id};Assets.Add(material);
                    TownServiceMirror.Assets.Register("clock/material"+id,material);TownServiceMirror.Assets.Register("clock/mesh"+id,mesh);
                    var renderer=source.gameObject.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.forceRenderingOff=page!=0;
                    address="merchant.cardbody|";
                }
                else
                {
                    source=Rect(part==0?"native face":"native price",gate,Vector2.zero,new Vector2(100,100));
                    source.gameObject.AddComponent<Canvas>().renderMode=RenderMode.WorldSpace;
                    source.gameObject.AddComponent<Image>().color=part==0?Color.red:Color.yellow;
                    address=part==0?"item.clock|":"merchant.row|";
                }
                source.localPosition=new Vector3((card%4)*.15f,card/4*.14f,-.04f);
                TownServiceMirror.RegisterTemplate(1,1,source,address:address);
                TownServiceMirror.RegisterModule(id,1,source,address:address);
                TownServiceMirror.SetRackMember(id,new TownRackStamp{Rack=1,Page=page},group);
                members.Add(new TownRackMember(id,page,false));cards.Add(id,source);
            }
            var clock=new TownRackState{Crank=2,Page=0,From=0,To=0,Members=members.ToArray()};
            TownServiceMirror.SetRack(1,clock);
            List<byte[]> first=Capture();
            byte[] withheld=first.Single(bytes=>TownServiceCodec.TryRead(bytes,bytes.Length,out var frame)&&frame!.Module==98);
            Receive(2,first.Where(bytes=>!ReferenceEquals(bytes,withheld)));TownServiceMirror.TickRemote(_=>observer);
            Check(Remote(2,1)!=null&&Remote(2,3)!=null,"actual rack and original warm modules instantiate");
            for(ushort id=3;id<98;id++)Check(RemoteGate(2,id).alpha==(id<51?1f:0f),"hidden prewarm never leaks a face or price");
            Check(Remote(2,52)!.Root.GetComponent<MeshRenderer>().forceRenderingOff,"hidden physical body uses its explicit page gate");
            clock=new TownRackState{Crank=2,Turn=1,Elapsed=.1f,Page=0,From=0,To=1,Members=members.ToArray()};
            rack.localRotation=Quaternion.Euler(0,15+360*TownRackState.Progress(.1f),0);
            TownServiceMirror.SetRack(1,clock);var beginning=Capture();Receive(2,beginning);TownServiceMirror.TickRemote(_=>observer);
            Receive(3,first);Receive(3,beginning);TownServiceMirror.TickRemote(_=>observer);
            Check(Quaternion.Angle(Quaternion.Inverse(observer.rotation)*Remote(3,1)!.Root.rotation,rack.localRotation)<.1f,"late join reconstructs the actual owner mid-turn phase");
            for(float wait=Time.unscaledTime+.08f;Time.unscaledTime<wait;)yield return null;TownServiceMirror.TickRemote(_=>observer);
            Check(Quaternion.Angle(Quaternion.Inverse(observer.rotation)*Remote(2,1)!.Root.rotation,Quaternion.Euler(0,15,0))<.1f,"missing one dependency keeps the complete outgoing page at rest");
            Receive(2,new[]{withheld});TownServiceMirror.TickRemote(_=>observer);
            float started=Time.unscaledTime;bool sawQuarter=false,sawBack=false;
            while(Time.unscaledTime-started<.96f)
            {
                TownServiceMirror.TickRemote(_=>observer);
                float elapsed=Time.unscaledTime-started;
                if(!sawQuarter&&elapsed>.20f)
                {
                    sawQuarter=true;
                    Check(Quaternion.Angle(Quaternion.Inverse(observer.rotation)*Remote(2,1)!.Root.rotation,Quaternion.Euler(0,15,0))>15f,"clock reconstructs the full revolution after coalesced owner poses");
                    Check(RemoteGate(2,3).alpha==1f&&RemoteGate(2,51).alpha==0f,"outgoing page remains whole before the opaque halfway point");
                    Vector3 corner = new Vector3(.07f,.08f,0);
                    Vector3 rackCorner = rack.InverseTransformPoint(cards[6].TransformPoint(corner));
                    Check(Vector3.Distance(Remote(2,6)!.Root.TransformPoint(corner),Remote(2,1)!.Root.TransformPoint(rackCorner))/scale<.001f,
                        "native card corners inherit the rotating rack at every world scale");
                }
                if(!sawBack&&elapsed>.46f)
                {
                    sawBack=true;
                    for(ushort id=3;id<=98;id++)Check(RemoteGate(2,id).alpha==(id<51?0f:1f),"all forty-eight face body price modules switch atomically");
                    Check(!Remote(2,52)!.Root.GetComponent<MeshRenderer>().forceRenderingOff,"incoming physical body appears with its face");
                }
                yield return null;
            }
            // Owner end clock may be delayed, but the explicit one-shot never repeats.
            Check(Quaternion.Angle(Quaternion.Inverse(observer.rotation)*Remote(2,1)!.Root.rotation,Quaternion.Euler(0,15,0))<.1f,"full revolution finishes at the exact authored rest pose at every rig scale");
            // Finish the owner clock, then verify a subsequent manual crank pull remains authored.
            rack.localRotation=Quaternion.Euler(0,15,0);
            clock=new TownRackState{Crank=2,Turn=1,Elapsed=.85f,Page=1,From=0,To=1,Members=members.ToArray()};
            TownServiceMirror.SetRack(1,clock);Receive(2,Capture());TownServiceMirror.TickRemote(_=>observer);
            crank.localRotation=Quaternion.Euler(0,15,0)*Quaternion.Euler(0,0,-25);
            Receive(2,Capture());TownServiceMirror.TickRemote(_=>observer);
            for(float wait=Time.unscaledTime+.12f;Time.unscaledTime<wait;)yield return null;TownServiceMirror.TickRemote(_=>observer);
            Check(Quaternion.Angle(Quaternion.Inverse(observer.rotation)*Remote(2,2)!.Root.rotation,crank.localRotation)<.1f,"idle manual lead pull is not overwritten by the previous clock: got "+(Quaternion.Inverse(observer.rotation)*Remote(2,2)!.Root.rotation).eulerAngles+" expected "+crank.localEulerAngles);
            var fade=content.gameObject.AddComponent<CanvasGroup>();fade.alpha=.37f;
            // The mirror publishes unchanged modules on a bounded cadence. A
            // newly added native ancestor fade can be captured on the next
            // cadence, so observe that publication instead of asserting in
            // the same frame that the source CanvasGroup changes.
            float fadeDeadline=Time.unscaledTime+1.5f;
            do
            {
                Receive(2,Capture());TownServiceMirror.TickRemote(_=>observer);
                if(Mathf.Abs(RemoteGate(2,51).alpha-.37f)<.001f)break;
                yield return null;
            }
            while(Time.unscaledTime<fadeDeadline);
            Check(Mathf.Abs(RemoteGate(2,51).alpha-.37f)<.001f,"page gate preserves independent native ancestor fades");
            cards[51].gameObject.SetActive(false);Receive(2,Capture());TownServiceMirror.TickRemote(_=>observer);
            Check(!Remote(2,51)!.Root.gameObject.activeInHierarchy,"page playback cannot revive a native-hidden card");
            cards[51].gameObject.SetActive(true);fade.alpha=1f;Receive(2,Capture());TownServiceMirror.TickRemote(_=>observer);
            clock=new TownRackState{Crank=2,Turn=2,Elapsed=.03f,Page=1,From=1,To=0,Members=members.ToArray()};
            rack.localRotation=Quaternion.Euler(0,15+360*TownRackState.Progress(.03f),0);TownServiceMirror.SetRack(1,clock);
            Receive(2,Capture());TownServiceMirror.TickRemote(_=>observer);
            // A hand update overtakes the finished-turn metadata and proves the next page
            // is already interactable. The receiver must never drag that held card around.
            rack.localRotation=Quaternion.Euler(0,15,0);cards[3].position=owner.TransformPoint(new Vector3(.6f,1.4f,-.8f));gates[3].alpha=1;
            TownServiceMirror.SetRackMember(3,new TownRackStamp{Rack=1,Page=0,Turn=2,Detached=true},gates[3]);
            cards[4].position=cards[3].position;gates[4].alpha=1;
            cards[4].GetComponent<MeshRenderer>().enabled=false;
            TownServiceMirror.SetRackMember(4,new TownRackStamp{Rack=1,Page=0,Turn=2,Detached=true},gates[4]);
            var held=Capture().Where(bytes=>TownServiceCodec.TryRead(bytes,bytes.Length,out var f)&&(f!.Module==3||f.Module==4)).ToArray();
            Receive(2,held);TownServiceMirror.TickRemote(_=>observer);
            Check(RemoteGate(2,3).alpha==1f,"overtaking held sample cannot inherit a hidden page gate");
            Check(!Remote(2,4)!.Root.GetComponent<MeshRenderer>().forceRenderingOff&&!Remote(2,4)!.Root.GetComponent<MeshRenderer>().enabled,
                "hidden prewarm body detaches immediately without replacing native renderer enablement");
            Check(Quaternion.Angle(Quaternion.Inverse(observer.rotation)*Remote(2,1)!.Root.rotation,Quaternion.Euler(0,15,0))<.1f,"newer held epoch supersedes delayed rack motion");
            Vector3 handPosition=observer.InverseTransformPoint(Remote(2,3)!.Root.position);
            Check(Vector3.Distance(handPosition,owner.InverseTransformPoint(cards[3].position))<.001f,"held card keeps the owner hand pose through rack recovery at every scale: "+scale+" got "+handPosition+" wanted "+owner.InverseTransformPoint(cards[3].position));
            foreach(ushort id in new ushort[]{3,4})TownServiceMirror.SetRackMember(id,new TownRackStamp{Rack=1,Page=0,Turn=2},gates[id]);
            clock=new TownRackState{Crank=2,Turn=3,Elapsed=.1f,Page=0,From=0,To=1,Members=members.ToArray()};
            rack.localRotation=Quaternion.Euler(0,15+360*TownRackState.Progress(.1f),0);TownServiceMirror.SetRack(1,clock);
            var turnThree=Capture();Receive(2,turnThree);TownServiceMirror.TickRemote(_=>observer);
            for(float wait=Time.unscaledTime+.2f;Time.unscaledTime<wait;)yield return null;TownServiceMirror.TickRemote(_=>observer);
            Quaternion quarter=Remote(2,1)!.Root.rotation;
            // Coalesced owner packets represent two valid consecutive .85-second turns.
            // The observer is still catching up, and must finish the displayed revolution.
            clock=new TownRackState{Crank=2,Turn=4,Elapsed=.85f,Page=0,From=1,To=0,Members=members.ToArray()};
            rack.localRotation=Quaternion.Euler(0,15,0);TownServiceMirror.SetRack(1,clock);
            Receive(2,Capture());Receive(2,turnThree);TownServiceMirror.TickRemote(_=>observer);
            Check(Quaternion.Angle(quarter,Remote(2,1)!.Root.rotation)<8f,"newer queued turn and reordered old packet do not reset an in-flight rack");
            for(float wait=Time.unscaledTime+.30f;Time.unscaledTime<wait;)yield return null;TownServiceMirror.TickRemote(_=>observer);
            Check(RemoteGate(2,51).alpha==1f&&RemoteGate(2,3).alpha==0f,"first queued revolution reveals its own destination behind the back");
            float drain=Time.unscaledTime;
            while(Time.unscaledTime-drain<1.35f){TownServiceMirror.TickRemote(_=>observer);yield return null;}
            Check(RemoteGate(2,3).alpha==1f&&RemoteGate(2,51).alpha==0f,"bounded replay completes consecutive turns in causal order");
            Check(Quaternion.Angle(Quaternion.Inverse(observer.rotation)*Remote(2,1)!.Root.rotation,Quaternion.Euler(0,15,0))<.1f,"queued completed clocks do not alias a full revolution or leave a rotated rack");
            // Two owner epochs were entirely coalesced; their intermediate from-page
            // must not suddenly replace the currently visible tray at the front.
            clock=new TownRackState{Crank=2,Turn=7,Elapsed=.15f,Page=1,From=1,To=0,Members=members.ToArray()};
            rack.localRotation=Quaternion.Euler(0,15+360*TownRackState.Progress(.15f),0);TownServiceMirror.SetRack(1,clock);
            Receive(2,Capture());TownServiceMirror.TickRemote(_=>observer);
            Check(RemoteGate(2,3).alpha==1f&&RemoteGate(2,51).alpha==0f,"skipped owner epochs preserve the actual outgoing front until the opaque midpoint");
            TownServiceMirror.EndSession();TownServiceMirror.ResetNetwork();
            UnityEngine.Object.DestroyImmediate(owner.gameObject);UnityEngine.Object.DestroyImmediate(observer.gameObject);
        }
    }
}
