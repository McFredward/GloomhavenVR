using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Cards;
using GloomhavenVR.Hands;
using GloomhavenVR.WorldUI;
using TMPro;
using UnityEngine.UI;
using System.Linq;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class MirrorProgram
{
    // Actual source samplers, original bodies and the engine renderer are bound;
    // catalogue/game rules are boundary inputs. No observer transform is corrected.
    private static IEnumerator NativeMerchant661(string variant)
    {
        foreach(bool stress in (variant=="old-global-subset-sequence" || variant=="old-root-acknowledgement")?new[]{true}:variant=="native-width"?new[]{false}:new[]{false,true})
        foreach(bool drop in stress && variant=="native"?new[]{false,true}:new[]{false})
        foreach(bool zero in stress?new[]{true}:new[]{true,false})
        foreach(float purchaseWidth in variant=="native-width"?new[]{.16f}:stress?new[]{.14f}:new[]{.14f,.16f})
        {
            TownServiceMirror.Shutdown(); NetPlayerActors.Peer=1; FlightTime655.Now=100f;
            Transform owner=Go("Merchant661 owner").transform, observer=Go("Merchant661 observer").transform;
            observer.position=Vector3.right*8f; TownServiceMirror.SharedFrameForRemote=_=>observer;
            Transform card=Go("actual prepared item",owner).transform; card.localPosition=new(.3f,1.2f,.1f);
            card.localRotation=Quaternion.Euler(8f,-12f,0f);
            NativeMerchant655 native=card.gameObject.AddComponent<NativeMerchant655>();
            ItemsPile.ItemChip actual=card.gameObject.AddComponent<ItemsPile.ItemChip>();
            RectTransform faceCanvas=(RectTransform)Go("FaceCanvas",card).transform;
            Canvas canvas=faceCanvas.gameObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;
            Transform front=NativeRow632(faceCanvas,"Leather Armor","item-card");
            RectTransform rect=(RectTransform)front;
            faceCanvas.sizeDelta=rect.rect.size;faceCanvas.localScale=Vector3.one*(.14f/rect.rect.width);
            faceCanvas.localPosition=Vector3.forward*-.0012f;
            NativeMerchant655.Canonicalize660(front,faceCanvas);
            Image background=front.GetComponent<Image>();
            Check(background!=null && background.sprite!=null,"actual exported merchant item background is resident before preparation");
            native.Bind661(front.gameObject,canvas,background);
            Transform body=TownServiceCardBody.Create(card).transform;body.name="Backing";body.localScale=new(.14f,.14f,1f);body.localPosition=Vector3.forward*.0012f;
            using(var assets=new TownServiceBinding(body))assets.Read(TownServiceMirror.Assets);
            var extras=new List<Transform>();var random=new System.Random(661);
            if(stress)for(int i=0;i<24;i++)
            {
                Transform extra=TownServiceCardBody.Create(card).transform;
                extra.name="Original body capacity member "+i;extra.localScale=body.localScale;
                extra.localPosition=new Vector3((float)random.NextDouble()*.035f,(float)random.NextDouble()*.035f,.003f+(float)random.NextDouble()*.02f);
                using(var assets=new TownServiceBinding(extra))assets.Read(TownServiceMirror.Assets);
                extras.Add(extra);
            }
            native.Prepare660();
            if(zero)native.PrepareMerchantPurchase(card.position,card.rotation);
            using(TownServiceMirror.UseStockLane())
            {
                TownServiceMirror.BeginSession(1,661,owner,owner);
                TownServiceMirror.RegisterTemplate(1,1,body,address:"inspectionbody.3e0f5c29.3e0f5c29.p|");
                TownServiceMirror.RegisterTemplate(1,2,front,address:"item.7|");
                TownServiceMirror.RegisterModule(11,1,body,address:"inspectionbody.3e0f5c29.3e0f5c29.p|");
                TownServiceMirror.RegisterModule(12,2,front,address:"item.7|");
                for(int i=0;i<extras.Count;i++)
                {
                    ushort id=(ushort)(20+i);string address="inspectionbody.3e0f5c29.3e0f5c29.p|capacity"+i;
                    TownServiceMirror.RegisterTemplate(1,id,extras[i],address:address);
                    TownServiceMirror.RegisterModule(id,id,extras[i],address:address);
                    TownServiceMirror.PrepareCardReturn(extras[i],actual.TryTownReturnMotion);
                }
                TownServiceMirror.PrepareCardReturn(body,actual.TryTownReturnMotion);TownServiceMirror.PrepareCardReturn(front,actual.TryTownReturnMotion);
            }
            Canvas.ForceUpdateCanvases();yield return null;
            for(int turn=0;turn<(stress?8:1);turn++)
            {FlightTime655.Now=100f+turn*.08f;FastCapture warm=CaptureFast();Receive(2,warm.Artwork);DeliverMotion(2,warm);TownServiceMirror.TickRemote(_=>observer);}
            float began=FlightTime655.Now+.08f;
            int peer=((Dictionary<int,int>)typeof(TownServiceMirror).GetField("StockKeys",PrivateStatic)!.GetValue(null)!)[2];
            var purchased=new ScenarioRuleLibrary.CItem {ID=7};native.AdoptMerchantPurchase(purchased);
            Check(ReferenceEquals(native.Item,purchased),"actual purchase adoption preserves authoritative native item identity");
            native.BeginMerchantPurchase(owner,card.position,card.rotation,purchaseWidth);
            var queue=new List<(int due,byte[] bytes)>();var pictures=new Dictionary<int,Color32[]>();var positions=new Dictionary<int,Vector3>();
            TownServiceMirror.RootAck661? rootAck=null;
            int packetIndex=0,partial=0,activated=-1;var packets=new List<int>();
            for(int frame=0;frame<=120;frame++)
            {
                FlightTime655.Now=began+frame/90f;FlightTime655.Delta=1f/90f;
                if(frame>0)native.StepNative655();
                positions[frame]=front.position;
                pictures[frame]=FlightPixels658(owner,8,"owner661-"+zero+"-stress"+stress+"-drop"+drop+"-width"+purchaseWidth+"-"+frame);
                Check(ArmorPixels661(front,pictures[frame])>=5,"actual source armor region is visibly textured at every frame="+frame+" width="+purchaseWidth);
                if(frame%6==0)
                {
                    FastCapture capture=CaptureFast();Receive(2,capture.Artwork);
                    if(!stress)DeliverMotion(2,capture);
                    else foreach(byte[] bytes in capture.Motion)
                    {
                        TownServiceMotionCodec.TryRead(bytes,bytes.Length,out var packet);
                        int count=packet!.Entries.Where(x=>x.Kind==10).Sum(x=>x.ReturnParts.Length);
                        packets.Add(count);
                        int index=packetIndex++;int delay=index==0?12:index==1?0:6;
                        File.AppendAllText(Path.Combine(_output,"multipart-packets661.csv"),frame+","+index+","+count+","+delay+"\n");
                        if(!drop || index!=0)queue.Add((frame+delay,bytes));
                    }
                }
                if(stress && frame==0)rootAck=TownServiceMirror.RootAck661.Capture();
                if(stress && frame==6)Check(rootAck!.PreservesNewerNativeRoot(),"frozen source snapshot does not acknowledge a newer native canvas recipe");
                foreach(var pending in queue.Where(x=>x.due<=frame).ToArray())
                {TownServiceMotionCodec.TryRead(pending.bytes,pending.bytes.Length,out var packet);TownServiceMirror.ReceiveMotion(2,packet!);queue.Remove(pending);}
                TownServiceMirror.TickRemote(_=>observer);
                Transform paintedBody=Remote(peer,11)!.Root,paintedFront=Remote(peer,12)!.Root;
                if(stress && !drop && frame==12)Check(paintedFront.gameObject.activeInHierarchy,"out-of-order complete multipart native receipt activates its exact source instant");
                if(stress && frame==30)Check(activated>=0,"out-of-order complete multipart native receipt arrives before native return ends");
                if(stress && !paintedFront.gameObject.activeInHierarchy)
                {
                    Check(!paintedBody.gameObject.activeInHierarchy && extras.Select((x,i)=>Remote(peer,(ushort)(20+i))!.Root).All(x=>!x.gameObject.activeInHierarchy),"partial frozen native receipt never exposes a grey body or blank front");partial++;yield return null;continue;
                }
                if(activated<0)activated=frame;
                Check(paintedBody.gameObject.activeInHierarchy && paintedFront.gameObject.activeInHierarchy,"merchant purchased body/front stay active at first and every flight frame="+frame+" zero="+zero+" body="+paintedBody.gameObject.activeInHierarchy+" front="+paintedFront.gameObject.activeInHierarchy);
                Check(float.IsFinite(paintedFront.lossyScale.x) && paintedFront.lossyScale.x>.00001f,"actual zero-scale preparation releases a finite printed face frame="+frame+" zero="+zero+" scale="+paintedFront.lossyScale.x);
                Vector3 nativeChild=body.InverseTransformPoint(front.position), paintedChild=paintedBody.InverseTransformPoint(paintedFront.position);
                Quaternion nativeFacing=Quaternion.Inverse(body.rotation)*front.rotation,paintedFacing=Quaternion.Inverse(paintedBody.rotation)*paintedFront.rotation;
                Vector3 nativeScale=new(front.lossyScale.x/body.lossyScale.x,front.lossyScale.y/body.lossyScale.y,front.lossyScale.z/body.lossyScale.z);
                Vector3 paintedScale=new(paintedFront.lossyScale.x/paintedBody.lossyScale.x,paintedFront.lossyScale.y/paintedBody.lossyScale.y,paintedFront.lossyScale.z/paintedBody.lossyScale.z);
                Check(Vector3.Distance(nativeChild,paintedChild)<.00005f && Quaternion.Angle(nativeFacing,paintedFacing)<.01f && Vector3.Distance(nativeScale,paintedScale)<.00005f,
                    "actual native canvas-relative front/body geometry remains coherent every frame="+frame+" zero="+zero+" offset="+Vector3.Distance(nativeChild,paintedChild)+" scale="+Vector3.Distance(nativeScale,paintedScale));
                Image rendered=paintedFront.GetComponent<Image>();
                Check(rendered.sprite==background.sprite,"original item artwork survives native preparation and return every frame");
                var sourceMaterial=TownServiceMaterial.Read(background.material,TownServiceMirror.Assets);
                var observerMaterial=TownServiceMaterial.Read(rendered.material,TownServiceMirror.Assets);
                Check(sourceMaterial.Same(observerMaterial),"every original native item material property and texture survives the return frame="+frame);
                Check(rendered.material.shader.name=="GloomhavenVR/FixtureNativeItem661" && rendered.material.GetFloat("_GreyOut")==0f,"fresh native item never acquires a grey-out material state");
                Color32[] a=pictures[stress?Math.Max(0,frame-6):frame],b=FlightPixels658(observer,9,"observer661-"+zero+"-stress"+stress+"-drop"+drop+"-width"+purchaseWidth+"-"+frame);
                Check(ArmorPixels661(paintedFront,b)>=5,"actual observer armor region is visibly textured at every frame="+frame+" width="+purchaseWidth);
                if(stress)File.AppendAllText(Path.Combine(_output,"multipart-positions661.csv"),frame+","+positions[Math.Max(0,frame-6)]+","+(paintedFront.position-observer.position)+","+positions[frame]+"\n");
                int different=0,union=0,colored=0;
                for(int i=0;i<a.Length;i++)
                {
                    if(Math.Max(a[i].r+a[i].g+a[i].b,b[i].r+b[i].g+b[i].b)<100)continue;
                    union++;if(Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b)>60)different++;
                    if(Math.Max(b[i].r,Math.Max(b[i].g,b[i].b))-Math.Min(b[i].r,Math.Min(b[i].g,b[i].b))>20)colored++;
                }
                Check(union>50 && colored>20 && different<=Math.Max(5,union*.1f),"actual merchant artwork matches through zero-scale purchase handoff frame="+frame+" zero="+zero+" union="+union+" colored="+colored+" different="+different);
                yield return null;
            }
            if(stress)Check(partial>0 && activated>6 && activated<40 && packets.Any(x=>x>0 && x<26),"out-of-order complete multipart native receipt makes bounded progress and atomically releases all canvas recipes");
        }
        TownServiceMirror.Shutdown();
    }
    private static int ArmorPixels661(Transform front,Color32[] picture)
    {
        var rect=(RectTransform)front;int colored=0;
        foreach(float x in new[]{.18f,.27f,.36f})foreach(float y in new[]{.3f,.4f,.5f})
        {
            Vector3 view=_camera.WorldToViewportPoint(rect.TransformPoint(new Vector3((x-rect.pivot.x)*rect.rect.width,(y-rect.pivot.y)*rect.rect.height,0f)));
            int px=Mathf.Clamp((int)(view.x*512),0,511),py=Mathf.Clamp((int)(view.y*384),0,383);Color32 c=picture[py*512+px];
            int hi=Math.Max(c.r,Math.Max(c.g,c.b)),lo=Math.Min(c.r,Math.Min(c.g,c.b));if(hi>30 && hi<230 && hi-lo>5)colored++;
        }
        return colored;
    }
    private static Color32[] FlightPixels658(Transform shared,int layer,string name)
    {
        foreach(GameObject fixture in Objects)if(fixture!=null)Layer(fixture.transform,30);
        Layer(shared,layer);
        foreach(Canvas canvas in shared.GetComponentsInChildren<Canvas>(true))canvas.worldCamera=_camera;
        _camera.cullingMask=1<<layer;_camera.transform.SetPositionAndRotation(shared.position+new Vector3(0f,1f,-5f),Quaternion.identity);
        _camera.orthographicSize=.65f;Canvas.ForceUpdateCanvases();
        var rt=new RenderTexture(512,384,24,RenderTextureFormat.ARGB32);var image=new Texture2D(512,384,TextureFormat.RGBA32,false);
        try
        {_camera.targetTexture=rt;_camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,512,384),0,0);image.Apply();
         File.WriteAllBytes(Path.Combine(_output,name+".png"),image.EncodeToPNG());return image.GetPixels32();}
        finally{RenderTexture.active=null;_camera.targetTexture=null;Object.DestroyImmediate(rt);Object.DestroyImmediate(image);}
    }
}
