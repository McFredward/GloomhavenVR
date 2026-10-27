using System;
using System.Collections;
using System.IO;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static partial class MenuDepthDesktopProgram
{
    public static IEnumerator OuterCarrier728(string output,Action<int> complete)
    {
        _count=0;DesktopEyeInput.Available=true;GloomhavenVR.Core.VRSession.IsRunning=true;
        using(var scene=new Scene())
        {
            scene.NativePixels725(4);
            scene.OriginalA.allowHDR=scene.OriginalB.allowHDR=false;scene.OriginalA.allowMSAA=scene.OriginalB.allowMSAA=false;
            scene.OriginalA.rect=new Rect(.08f,.12f,.83f,.76f);
            scene.OriginalA.gameObject.AddComponent<MenuDepthAnchorsImageEffect724>();
            scene.ModelCamera.allowHDR=scene.ModelCamera.allowMSAA=false;
            scene.ModelCamera.gameObject.AddComponent<MenuDepthAnchorsImageEffect724>();
            Transform holder=scene.ModelCamera.GetComponent<Character3DDisplayManager>().Holder;
            foreach(Transform child in holder)child.gameObject.SetActive(false);
            var actor=new GameObject("original native main actor ground");actor.transform.SetParent(holder,false);
            actor.transform.position=scene.ModelCamera.transform.TransformPoint(new Vector3(0,-1.2f,2.56f));
            var body=GameObject.CreatePrimitive(PrimitiveType.Cube);body.layer=8;body.transform.SetParent(actor.transform,false);
            body.transform.localScale=new Vector3(.55f,1.65f,.55f);
            var bodyMaterial=new Material(Shader.Find("Unlit/Color"));bodyMaterial.color=Color.cyan;body.GetComponent<MeshRenderer>().sharedMaterial=bodyMaterial;
            scene.ModelCamera.GetComponent<Character3DDisplayManager>().BindOriginal(actor.transform);
            RawImage raw=null!;
            foreach(var image in Object.FindObjectsOfType<RawImage>())if(ReferenceEquals(image.texture,scene.ModelTarget))raw=image;
            Check(raw!=null,"the outer carrier uses the original native character RawImage");
            scene.Flat.Begin();Canvas.ForceUpdateCanvases();scene.OriginalA.Render();scene.OriginalB.Render();
            RectTransform nativeRoot=(RectTransform)raw!.canvas.rootCanvas.transform;
            raw.rectTransform.anchoredPosition=new Vector2(-nativeRoot.rect.width*.5f+raw.rectTransform.rect.width*.6f,0);
            var ordinary=new GameObject("ordinary native UI beyond old screen",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));ordinary.layer=5;
            ordinary.transform.SetParent(nativeRoot,false);var ordinaryRect=(RectTransform)ordinary.transform;
            ordinaryRect.sizeDelta=new Vector2(400,200);ordinaryRect.anchoredPosition=new Vector2(-nativeRoot.rect.width*.85f,0);ordinary.GetComponent<Image>().color=Color.blue;
            scene.Head.orthographic=false;scene.Head.fieldOfView=60;scene.Head.nearClipPlane=.01f;
            scene.HeadTarget.Release();scene.HeadTarget.width=640;scene.HeadTarget.height=480;scene.HeadTarget.Create();
            Vector3 screenPosition=scene.Screen.position,screenScale=scene.Screen.localScale;Quaternion screenRotation=scene.Screen.rotation;
            var corner=new GameObject("original native top-right anchored control",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));corner.layer=5;
            corner.transform.SetParent(nativeRoot,false);var cornerRect=(RectTransform)corner.transform;cornerRect.anchorMin=cornerRect.anchorMax=Vector2.one;
            cornerRect.sizeDelta=new Vector2(30,30);cornerRect.anchoredPosition=new Vector2(-40,-40);corner.GetComponent<Image>().color=Color.yellow;
            Canvas.ForceUpdateCanvases();
            Vector2 originalRawSize=raw.rectTransform.sizeDelta;Matrix4x4 originalCanvas=nativeRoot.localToWorldMatrix;
            Rect originalRootRect=nativeRoot.rect;Matrix4x4 originalCorner=cornerRect.localToWorldMatrix;bool liveLayoutExact=true;
            Camera.CameraCallback nativeLayout=c=>{if(c==scene.OriginalA)liveLayoutExact&=nativeRoot.rect==originalRootRect&&Same(cornerRect.localToWorldMatrix,originalCorner);};
            Camera.onPostRender+=nativeLayout;
            try
            {
                for(int side=0;side<2;side++)
                {
                    body.transform.localPosition=new Vector3(0,1.2f,side==0?-.35f:.35f);
                    int frame=Time.frameCount;while(Time.frameCount==frame)yield return null;
                    scene.Eyes(new Vector3(.3f,0,side==0?1.88f:2.12f),side!=0);
                    for(int eye=0;eye<2;eye++)
                    {
                        Vector3 position=scene.Head.GetStereoViewMatrix(eye==0?Camera.StereoscopicEye.Left:Camera.StereoscopicEye.Right).inverse.MultiplyPoint3x4(Vector3.zero);
                        Check(MenuDepthScreenProjection.TryMap(scene.Screen,scene.OriginalA,raw.rectTransform,position,out Vector3 nativeEye),"original partial UI viewport admits the actual outer-figure eye");
                        Vector3 origin=scene.ModelCamera.transform.InverseTransformPoint(actor.transform.position);
                        Check(MenuDepthCharacterProjection.TryBuild(scene.ModelCamera,holder,raw.rectTransform,raw.uvRect,nativeEye,origin,out var view),"independent native main-actor calibration establishes the actual physical figure");
                        Vector3 physical=PhysicalBody728(scene,raw,view,body.transform.position);
                        // The final head now reads the real stored eye view.
                        // Aim both supplied eyes at the same fixed body before
                        // the first eye prepares this complete stereo pair.
                        for(int observedEye=0;observedEye<2;observedEye++)
                        {
                            var selected=observedEye==0?Camera.StereoscopicEye.Left:Camera.StereoscopicEye.Right;
                            Vector3 point=scene.Head.GetStereoViewMatrix(selected).inverse.MultiplyPoint3x4(Vector3.zero);
                            Quaternion facing=Quaternion.LookRotation(physical-point,Vector3.up);
                            scene.Head.SetStereoViewMatrix(selected,Matrix4x4.Scale(new Vector3(1,1,-1))*Matrix4x4.TRS(point,facing,Vector3.one).inverse);
                        }
                        scene.Head.transform.SetPositionAndRotation(position,Quaternion.LookRotation(physical-position,Vector3.up));
                        int modelBefore=scene.ModelCamera.GetComponent<MenuDepthAnchorsImageEffect724>().Draws;
                        int uiBefore=scene.OriginalA.GetComponent<MenuDepthAnchorsImageEffect724>().Draws;
                        scene.Render(eye!=0);
                        Check(scene.ModelCamera.GetComponent<MenuDepthAnchorsImageEffect724>().Draws-modelBefore==(eye==0?2:0)
                            &&scene.OriginalA.GetComponent<MenuDepthAnchorsImageEffect724>().Draws-uiBefore==(eye==0?2:0),
                            "each outer eye preserves native model and UI image effects without an extra render");
                        Check(liveLayoutExact,"expanded GPU rendering retains original native root dimensions and edge-anchored controls during the capture itself");
                        Check(scene.Flat.Right!=null&&scene.Flat.Right.width==scene.HeadTarget.width&&scene.Flat.Right.height==scene.HeadTarget.height,
                            "actual original character foreground uses the complete fixed head raster outside the old FlatScreen carrier");
                        Check(scene.Screen.position==screenPosition&&scene.Screen.localScale==screenScale&&scene.Screen.rotation==screenRotation
                            &&raw.rectTransform.sizeDelta==originalRawSize&&Same(nativeRoot.localToWorldMatrix,originalCanvas),
                            "outer visual carrier leaves original native window hit layout and world pose exact");
                        Texture2D actual=Read(scene.HeadTarget,Path.Combine(output,"outer-"+side+"-"+eye+".png"));
                        Texture2D expected=ReferenceBody728(scene,raw,view,body,Path.Combine(output,"reference-"+side+"-"+eye+".png"));
                        Color32[] a=actual.GetPixels32(),b=expected.GetPixels32();int total=0,found=0,intersection=0,union=0,outside=0,ordinaryLeak=0;
                        for(int i=0;i<a.Length;i++)
                        {
                            bool ac=Cyan728(a[i]),bc=Cyan728(b[i]);if(ac)found++;if(bc)total++;if(ac&&bc)intersection++;if(ac||bc)union++;
                            if(!ac&&!(a[i].b>100&&a[i].g<60))continue;
                            Ray ray=scene.Head.ViewportPointToRay(new Vector3((i%actual.width+.5f)/actual.width,(i/actual.width+.5f)/actual.height));
                            var plane=new Plane(scene.Screen.forward,scene.Screen.position);
                            if(plane.Raycast(ray,out float distance))
                            {
                                Vector3 local=scene.Screen.InverseTransformPoint(ray.GetPoint(distance));
                                if(Mathf.Abs(local.x)>.501f||Mathf.Abs(local.y)>.501f){if(ac)outside++;else ordinaryLeak++;}
                            }
                        }
                        File.AppendAllText(Path.Combine(output,"outer-coverage.txt"),side+","+eye+",sampled="+scene.Flat.SampleFrame+",actual="+found+",reference="+total+",outside="+outside+",IoU="+(intersection/(float)Math.Max(1,union))+",flatLeak="+ordinaryLeak+"\n");
                        Check(total>100&&outside>30,"real native foreground pixels remain visible outside the whole original window from either physical side");
                        Check(found>=total*.95f&&intersection/(float)Math.Max(1,union)>.95f,"the complete original 3D figure matches its independent physical geometry instead of outer-frame clipping");
                        Check(ordinaryLeak==0,"expansion never reveals ordinary native UI beyond the original screen");
                        LateMeshPanel728(scene,physical,b,eye!=0,Path.Combine(output,"panel-"+side+"-"+eye));
                        Object.DestroyImmediate(actual);Object.DestroyImmediate(expected);
                    }
                }
            }
            finally {Camera.onPostRender-=nativeLayout;Object.DestroyImmediate(bodyMaterial);}
        }
        complete(_count);
    }

    private static void LateMeshPanel728(Scene scene,Vector3 body,Color32[] reference,bool right,string output)
    {
        var camera=new GameObject("second original native UI projector").AddComponent<Camera>();camera.enabled=false;
        camera.transform.position=new Vector3(700,700,700);camera.orthographic=true;camera.orthographicSize=1;camera.allowHDR=camera.allowMSAA=false;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;camera.cullingMask=1<<29;
        var target=new RenderTexture(224,224,24);target.Create();camera.targetTexture=target;
        var root=new GameObject("second actual native UI canvas",typeof(RectTransform),typeof(Canvas));root.layer=29;
        var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=3;
        var image=new GameObject("second actual original printed UI",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));image.layer=29;
        image.transform.SetParent(root.transform,false);((RectTransform)image.transform).sizeDelta=new Vector2(224,224);image.GetComponent<Image>().color=Color.blue;
        Canvas.ForceUpdateCanvases();camera.Render();
        var quad=GameObject.CreatePrimitive(PrimitiveType.Quad);quad.layer=27;quad.name="second native visual mesh carrier";
        var material=new Material(Shader.Find("Sprites/Default")){mainTexture=target,renderQueue=3100};quad.GetComponent<MeshRenderer>().sharedMaterial=material;
        quad.transform.rotation=scene.Head.transform.rotation;quad.transform.localScale=new Vector3(.4f,.5f,1);
        var carrier=new MenuDepthDesktopCarrier(quad.transform,material,target,target);
        Camera.CameraCallback begin=c=>{if(c==scene.Head)carrier.Draw(target,new Rect(0,0,1,1),1);};
        Camera.CameraCallback end=c=>{if(c==scene.Head)carrier.EndDraw();};Camera.onPreCull+=begin;Camera.onPostRender+=end;
        try
        {
            for(int side=0;side<2;side++)
            {
                quad.transform.position=body+scene.Head.transform.forward*(side==0?.13f:-.13f);
                scene.Render(right);Texture2D pixels=Read(scene.HeadTarget,output+(side==0?"-behind.png":"-in-front.png"));
                Color32[] actual=pixels.GetPixels32();int total=0,cyan=0,blue=0;
                for(int i=0;i<actual.Length;i++)if(Cyan728(reference[i])){total++;if(Cyan728(actual[i]))cyan++;if(actual[i].b>100&&actual[i].g<60)blue++;}
                if(side==0)Check(cyan>=total*.95f,"a later actual native UI mesh behind the foreground cannot paint over the character");
                else Check(blue>=total*.95f&&cyan<total*.05f,"a genuinely nearer actual native UI mesh still occludes the character");
                Object.DestroyImmediate(pixels);
            }
        }
        finally
        {
            Camera.onPreCull-=begin;Camera.onPostRender-=end;carrier.Dispose();Object.DestroyImmediate(quad);Object.DestroyImmediate(material);
            Object.DestroyImmediate(root);Object.DestroyImmediate(camera.gameObject);target.Release();Object.DestroyImmediate(target);
        }
    }

    private static bool Cyan728(Color32 c)=>c.r<70&&c.g>100&&c.b>100;
    private static Vector3 PhysicalBody728(Scene scene,RawImage raw,MenuDepthCharacterProjection.View view,Vector3 nativePoint)
    {
        Vector3 graphic=raw.rectTransform.TransformPoint(view.NativeToGraphic.MultiplyPoint3x4(nativePoint));
        Camera ui=scene.OriginalA;Vector3 uv=ui.WorldToViewportPoint(graphic);Rect r=ui.rect;
        Vector3 plane=scene.Screen.TransformPoint(new Vector3(r.x+uv.x*r.width-.5f,r.y+uv.y*r.height-.5f));
        float nativeWidth=(ui.ViewportToWorldPoint(new Vector3(1,.5f,uv.z))-ui.ViewportToWorldPoint(new Vector3(0,.5f,uv.z))).magnitude;
        float nativeHeight=(ui.ViewportToWorldPoint(new Vector3(.5f,1,uv.z))-ui.ViewportToWorldPoint(new Vector3(.5f,0,uv.z))).magnitude;
        float units=Mathf.Sqrt(nativeWidth/(scene.Screen.TransformVector(Vector3.right).magnitude*r.width)
            *nativeHeight/(scene.Screen.TransformVector(Vector3.up).magnitude*r.height));
        float depth=Vector3.Dot(graphic-raw.rectTransform.TransformPoint(raw.rectTransform.rect.center),ui.transform.forward)/units;
        return plane+scene.Screen.forward*depth;
    }
    private static Texture2D ReferenceBody728(Scene scene,RawImage raw,MenuDepthCharacterProjection.View view,GameObject native,string path)
    {
        var reference=GameObject.CreatePrimitive(PrimitiveType.Cube);reference.layer=28;
        Vector3 centre=PhysicalBody728(scene,raw,view,native.transform.position);
        Vector3 x=PhysicalBody728(scene,raw,view,native.transform.position+Vector3.right)-centre;
        Vector3 y=PhysicalBody728(scene,raw,view,native.transform.position+Vector3.up)-centre;
        Vector3 z=PhysicalBody728(scene,raw,view,native.transform.position+Vector3.forward)-centre;
        reference.transform.SetPositionAndRotation(centre,Quaternion.LookRotation(z,y));
        reference.transform.localScale=new Vector3(x.magnitude*native.transform.lossyScale.x,y.magnitude*native.transform.lossyScale.y,z.magnitude*native.transform.lossyScale.z);
        reference.GetComponent<MeshRenderer>().sharedMaterial=native.GetComponent<MeshRenderer>().sharedMaterial;
        var camera=new GameObject("independent actual physical body reference").AddComponent<Camera>();camera.CopyFrom(scene.Head);camera.enabled=false;camera.cullingMask=1<<28;
        camera.transform.SetPositionAndRotation(scene.Head.transform.position,scene.Head.transform.rotation);
        var target=new RenderTexture(scene.HeadTarget.width,scene.HeadTarget.height,24);target.Create();camera.targetTexture=target;
        try {camera.Render();return Read(target,path);}
        finally {Object.DestroyImmediate(reference);Object.DestroyImmediate(camera.gameObject);target.Release();Object.DestroyImmediate(target);}
    }
}
