using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Cards;
using GloomhavenVR.Hands;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class MirrorProgram
{
    // Three independent physical originals create contention for the real numeric
    // budget. Each card's native return registration is shared by its front/body.
    // The source geometry and engine renderer are real; catalogue/game rules are
    // boundary inputs. No observer transform is corrected by the test.
    private static IEnumerator Flight658(bool prepared)
    {
        TownServiceMirror.Shutdown(); NetPlayerActors.Peer=1; FlightTime655.Now=100f;
        Transform owner=Go("Flight658 source shared frame").transform;
        Transform observer=Go("Flight658 observer shared frame").transform;
        observer.position=Vector3.right*8f;
        TownServiceMirror.SharedFrameForRemote=_=>observer;
        var faces=new Transform[3]; var bodies=new Transform[3]; var natives=new VRCard[3];
        bool moving=false;
        using(TownServiceMirror.UseStockLane())
        {
            TownServiceMirror.BeginSession(1,658,owner,owner);
            for(int i=0;i<3;i++)
            {
                Transform card=Go("Physical native card "+i,owner).transform;
                card.localPosition=new Vector3(0f,1f+(i-1)*.32f,0f);
                natives[i]=card.gameObject.AddComponent<VRCard>();
                faces[i]=Source(card); faces[i].localPosition=new Vector3(0f,0f,-.0012f);
                faces[i].localRotation=Quaternion.identity; faces[i].localScale=Vector3.one*.0005f;
                // This is the declared physical-body mesh boundary, with the
                // native item-card thickness and exact captured material channel.
                GameObject body=GameObject.CreatePrimitive(PrimitiveType.Cube); Objects.Add(body);
                Object.DestroyImmediate(body.GetComponent<Collider>());
                body.name="Original card body "+i; body.transform.SetParent(card,false);
                body.transform.localPosition=new Vector3(0f,0f,.0012f);
                body.transform.localScale=new Vector3(.195f,.125f,.0022f); bodies[i]=body.transform;
                Material original=new Material(Shader.Find("Unlit/Color")){color=new Color(.32f,.29f,.25f,1f)};
                Assets.Add(original); body.GetComponent<MeshRenderer>().sharedMaterial=original;
                TownServiceMirror.Assets.Register("flight658/body-material-"+i,original);
                TownServiceMirror.Assets.Register("flight658/body-mesh-"+i,body.GetComponent<MeshFilter>().sharedMesh);
                int slot=i;
                bool Sample(Transform source,Transform frame,VRHand? hand,out uint revision,out float[] values)
                {
                    if(moving)return natives[slot].CaptureNativeTownReturn655(source,frame,hand,out revision,out values);
                    revision=0;values=Array.Empty<float>();return false;
                }
                TownServiceMirror.RegisterTemplate(1,(ushort)(i+1),bodies[i],address:"map.cardbody|"+i);
                TownServiceMirror.RegisterTemplate(1,(ushort)(i+4),faces[i],address:"face."+(6580+i)+"|");
                // Body modules precede print modules, reproducing the actual
                // independently published originals rather than an interleaved toy.
                if(prepared)
                { TownServiceMirror.PrepareCardReturn(bodies[i],Sample);TownServiceMirror.PrepareCardReturn(faces[i],Sample); }
                else
                { TownServiceMirror.RegisterCardReturn(bodies[i],Sample);TownServiceMirror.RegisterCardReturn(faces[i],Sample); }
            }
        }
        using(TownServiceMirror.UseStockLane())
        {
            for(int i=0;i<3;i++)TownServiceMirror.RegisterModule((ushort)(10+i),(ushort)(i+1),bodies[i],address:"map.cardbody|"+i);
            for(int i=0;i<3;i++)TownServiceMirror.RegisterModule((ushort)(13+i),(ushort)(i+4),faces[i],address:"face."+(6580+i)+"|");
        }
        Canvas.ForceUpdateCanvases();yield return null;
        FastCapture baseline=CaptureFast();Receive(2,baseline.Artwork);DeliverMotion(2,baseline);
        TownServiceMirror.TickRemote(_=>observer);
        var keys=(Dictionary<int,int>)typeof(TownServiceMirror).GetField("StockKeys",PrivateStatic)!.GetValue(null)!;
        int peer=keys[2];
        for(int i=0;i<3;i++)
            Check(Remote(peer,(ushort)(10+i))!=null && Remote(peer,(ushort)(13+i))!=null,"exact front/body baselines exist before the native return");
        moving=true;
        for(int i=0;i<3;i++)natives[i].BeginNative655(owner.TransformPoint(new Vector3(.28f,1f+(i-1)*.32f,0f)),.55f);
        var trace=new System.Text.StringBuilder("frame,card,sourceAge,bodyDistance,faceDistance,coherence,frontVisible,bodyVisible\n");
        for(int frame=1;frame<=36;frame++)
        {
            FlightTime655.Now=100f+frame/90f;FlightTime655.Delta=1f/90f;
            for(int i=0;i<3;i++)natives[i].StepNative655();
            if(frame%7==0)
            {
                // The real fast stream shares this event with changing native
                // hover/material numbers. Incompressible numeric property input
                // exercises actual compression trimming, not a fake packet split.
                var random=new System.Random(frame);
                foreach(Transform print in faces)
                    foreach(var graphic in print.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                        graphic.color=new Color((float)random.NextDouble(),(float)random.NextDouble(),(float)random.NextDouble(),1f);
                // Numeric publication must open prepared originals without a
                // slower immutable-artwork/repair receipt authoring a second pose.
                if(prepared)TownServiceMirror.CaptureMotion((bytes,length,identity)=>
                {Check(TownServiceMotionCodec.TryRead(bytes,length,out var packet),"actual numeric packet decodes");TownServiceMirror.ReceiveMotion(2,packet!);});
                else DeliverMotion(2,CaptureFast());
            }
            TownServiceMirror.TickRemote(_=>observer);
            if(frame<7){yield return null;continue;}
            for(int i=0;i<3;i++)
            {
                Transform body=Remote(peer,(ushort)(10+i))!.Root,face=Remote(peer,(ushort)(13+i))!.Root;
                float bodyDistance=Vector3.Distance(body.position,observer.TransformPoint(owner.InverseTransformPoint(bodies[i].position)));
                float faceDistance=Vector3.Distance(face.position,observer.TransformPoint(owner.InverseTransformPoint(faces[i].position)));
                Vector3 expectedOffset=body.rotation * (Vector3.forward * (.0024f * body.lossyScale.x/.195f));
                float coherence=Vector3.Distance(body.position-face.position,expectedOffset);
                trace.AppendLine(frame+","+i+","+((frame-7)/90f)+","+bodyDistance+","+faceDistance+","+coherence+","+face.gameObject.activeInHierarchy+","+body.gameObject.activeInHierarchy);
                if(prepared && i==0 && frame==7)
                    Check(face.gameObject.activeInHierarchy && body.gameObject.activeInHierarchy,"prepared original opens on the first exact native flight");
                Check(coherence<.00005f && face.gameObject.activeInHierarchy==body.gameObject.activeInHierarchy,
                    "each moving original retains its printed front on the same first render card="+i+" frame="+frame+" error="+coherence);
            }
            if(frame is 8 or 15 or 22 or 29)
            {
                // Compare the whole physical original after moving the source back
                // to the same delayed owner instant. World-pose coherence above is
                // evaluated before this screenshot and cannot be hidden by cameras.
                Color32[] picture=FlightPixels658(observer,9,"flight658-observer-"+frame);
                Check(FlightInk658(picture)>1200,"combined original flight renders visible native print and body pixels");
            }
            yield return null;
        }
        File.WriteAllText(Path.Combine(_output,"flight658-coherence.csv"),trace.ToString());
        TownServiceMirror.Shutdown();
    }
    private static int FlightInk658(Color32[] pixels)
    {int count=0;foreach(Color32 pixel in pixels)if(pixel.r>40 && pixel.g>40 && pixel.b>40)count++;return count;}
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
