using System;
using System.Collections;
using System.IO;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static partial class MenuDepthDesktopProgram
{
    private static Rect CyanBounds752(Texture2D image)
    {
        int x0=image.width,y0=image.height,x1=0,y1=0,count=0;
        Color32[] pixels=image.GetPixels32();
        for(int y=0;y<image.height;y++)for(int x=0;x<image.width;x++)
        {var c=pixels[y*image.width+x];if(c.g>100&&c.b>100&&c.r<50){x0=Math.Min(x0,x);x1=Math.Max(x1,x);y0=Math.Min(y0,y);y1=Math.Max(y1,y);count++;}}
        Check(count>50,"actual coordinator retains the complete visible creation figure in final head pixels");
        return Rect.MinMaxRect(x0,y0,x1+1,y1+1);
    }
    private static Rect ProjectBox752(Matrix4x4 matrix, Bounds box)
    {
        float x0=float.PositiveInfinity,y0=x0,x1=float.NegativeInfinity,y1=x1;
        for(int i=0;i<8;i++)
        {
            Vector3 p=matrix.MultiplyPoint3x4(box.center+Vector3.Scale(box.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)));
            x0=Mathf.Min(x0,p.x);x1=Mathf.Max(x1,p.x);y0=Mathf.Min(y0,p.y);y1=Mathf.Max(y1,p.y);
        }
        return Rect.MinMaxRect(x0,y0,x1,y1);
    }
    private static Rect HeadBox753(Camera head, Matrix4x4 matrix, Bounds box)
    {
        float x0=float.PositiveInfinity,y0=x0,x1=float.NegativeInfinity,y1=x1;
        for(int i=0;i<8;i++)
        {
            Vector3 p=head.WorldToScreenPoint(matrix.MultiplyPoint3x4(box.center+Vector3.Scale(box.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1))));
            x0=Mathf.Min(x0,p.x);x1=Mathf.Max(x1,p.x);y0=Mathf.Min(y0,p.y);y1=Mathf.Max(y1,p.y);
        }
        return Rect.MinMaxRect(x0,y0,x1,y1);
    }
    public static IEnumerator HeadCreation752(string output,Action<int> complete)
    {
        _count=0;DesktopEyeInput.Available=true;
        using(var scene=new Scene())
        {
            MenuDepthCharacters.Shutdown();scene.HideSpatial730();
            foreach(var root in new[]{scene.RootA,scene.RootB})foreach(var graphic in root.GetComponentsInChildren<Graphic>(true))graphic.enabled=false;
            scene.Screen.GetComponent<Renderer>().enabled=false;
            scene.HeadTarget.Release();scene.HeadTarget.width=1280;scene.HeadTarget.height=960;scene.HeadTarget.Create();
            scene.Head.orthographic=false;scene.Head.fieldOfView=60;scene.Head.aspect=4f/3;scene.Head.nearClipPlane=.01f;scene.Head.farClipPlane=20;
            scene.Head.cullingMask=1<<27;scene.OriginalA.cullingMask=1<<24;scene.OriginalA.enabled=false;
            var owner=ConfirmationCanvas745("native character creation owner",24,scene.OriginalA,new Vector2(1920,1080),new Vector3(0,0,2.8f));
            owner.localScale=Vector3.one*.0015f;
            var creator=owner.gameObject.AddComponent<UICharacterCreatorWindow>();
            var name=new GameObject("native name page",typeof(RectTransform),typeof(UIWindow));name.transform.SetParent(owner,false);
            var quest=new GameObject("native quest page",typeof(RectTransform),typeof(UIWindow),typeof(UICharacterCreatorPersonalQuestStep));quest.transform.SetParent(owner,false);
            creator.BindCreationPages(name.GetComponent<UIWindow>(),quest.GetComponent<UICharacterCreatorPersonalQuestStep>());
            // Native authored dimensions. Visible keyboard position is an explicit
            // animation input; no original gameplay controller runs in the fixture.
            var keyboard=ReadyGraphic746(name.transform,"native keyboard",new Vector2(726,286),new Vector2(0,-220),Color.red,24);
            keyboard.gameObject.AddComponent<UIKeyboard>();
            var help=ReadyGraphic746(quest.transform,"native quest help line",new Vector2(800,50),new Vector2(300,-295),Color.white,24);
            quest.GetComponent<UICharacterCreatorPersonalQuestStep>().BindHelp(help);
            var raw=ReadyGraphic746(owner,"native preview rectangle",new Vector2(1476.5625f,1080),Vector2.zero,Color.white,24);
            var rawRect=raw.rectTransform;Object.DestroyImmediate(raw);var preview=rawRect.gameObject.AddComponent<RawImage>();preview.texture=scene.ModelTarget;
            Transform holder=scene.ModelCamera.GetComponent<Character3DDisplayManager>().Holder;
            foreach(Transform child in holder)child.gameObject.SetActive(false);
            var ground=new GameObject("native actor ground");ground.transform.SetParent(holder,false);ground.transform.position=scene.ModelCamera.transform.TransformPoint(new Vector3(0,-1.2f,2.56f));
            var body=GameObject.CreatePrimitive(PrimitiveType.Cube);body.layer=8;body.transform.SetParent(ground.transform,false);
            var material=new Material(Shader.Find("Unlit/Color"));material.color=Color.cyan;body.GetComponent<Renderer>().sharedMaterial=material;
            var bear=GameObject.CreatePrimitive(PrimitiveType.Cube);bear.layer=8;bear.transform.SetParent(ground.transform,false);
            // Native guildmaster companion offset plus a conservative original
            // bear body/armor box. These are declared scene inputs, not new fit rules.
            bear.transform.localPosition=new Vector3(.48767f,.61304f,.5195f);bear.transform.localScale=new Vector3(.98f,1.32f,1.77f);
            bear.GetComponent<Renderer>().sharedMaterial=material;bear.SetActive(false);
            scene.ModelCamera.GetComponent<Character3DDisplayManager>().BindOriginal(ground.transform);
            var display=ConfirmationCanvas745("creation captured photograph",27,scene.Head,owner.rect.size,owner.position);display.localScale=owner.localScale;
            var photograph=display.gameObject.AddComponent<RawImage>();photograph.texture=scene.Ui;
            var entry=PanelSupersample.BindHead(owner,scene.OriginalA,scene.Ui,photograph);
            scene.OriginalA.targetTexture=scene.Ui;scene.OriginalA.orthographic=true;scene.OriginalA.aspect=1920f/1080;scene.OriginalA.orthographicSize=1080*owner.localScale.y*.5f;
            scene.OriginalA.transform.SetPositionAndRotation(owner.position-Vector3.forward*2,Quaternion.identity);
            scene.OriginalA.ResetProjectionMatrix();Canvas.ForceUpdateCanvases();MenuDepthCharacters.Tick();
            Check(MenuDepthSources.HasContent(owner),"new native creator preview is registered for the window coordinator");
            Vector3 initialGround=ground.transform.position;Vector3 rawPosition=rawRect.position;
            // BindHead supplies an already-engaged photograph, as the other
            // coordinator fixtures do. Let its first cached mono batch retire
            // before testing page transitions of that established window.
            body.transform.localScale=new Vector3(.9f,1.23f,.3f);body.transform.localPosition=new Vector3(0,.615f,0);
            scene.ModelCamera.Render();scene.OriginalA.Render();scene.Eyes(Vector3.zero);scene.Render(false);
            yield return null;
            try
            {
                for(int page=0;page<3;page++)for(int actor=0;actor<(page==1?4:2);actor++)
                {
                    name.GetComponent<UIWindow>().IsOpen=page==1;quest.GetComponent<UIWindow>().IsOpen=page==2;
                    keyboard.enabled=page==1;help.enabled=page==2;
                    float height=actor==0||actor==3?1.23f:2.26f;body.transform.localScale=new Vector3(actor==2?1.72f:.9f,height,.3f);body.transform.localPosition=new Vector3(0,height*.5f,0);
                    bear.SetActive(actor==3);
                    Canvas.ForceUpdateCanvases();scene.ModelCamera.Render();scene.OriginalA.Render();MenuDepthCharacters.Tick();scene.Eyes(Vector3.zero);scene.Render(false);
                    Check(entry.MenuDepthDisplay!=null,"actual converted-window coordinator captures the live preview for the head");
                    Check(MenuDepthCharacterProjection.TryBuild(scene.ModelCamera,holder,rawRect,preview.uvRect,scene.Head.transform.position,
                        scene.ModelCamera.transform.InverseTransformPoint(ground.transform.position),out var view),"creation has a valid live native projection");
                    Rect local=ProjectBox752(view.NativeToGraphic,body.GetComponent<Renderer>().bounds);
                    if(page==0)
                        Check(Same(rawRect.localToWorldMatrix*view.NativeToGraphic,BodyMap730(scene.ModelCamera,ground.transform,rawRect)),"ordinary character preview keeps its original native metric and alignment");
                    else if(page==1)
                    {
                        Rect all=local;
                        if(bear.activeSelf){Rect companion=ProjectBox752(view.NativeToGraphic,bear.GetComponent<Renderer>().bounds);all=Rect.MinMaxRect(Mathf.Min(all.xMin,companion.xMin),Mathf.Min(all.yMin,companion.yMin),Mathf.Max(all.xMax,companion.xMax),Mathf.Max(all.yMax,companion.yMax));}
                        Check(all.xMax < -363-20,"the entire short and tall creation figure clears every native keyboard key");
                        Check(local.height>height*180f,"naming preview uses the free vertical space with an upright body frame");
                        Check(all.xMin>=rawRect.rect.xMin&&all.xMax<=rawRect.rect.xMax,"wide bodies and the positive-offset companion fit the original portrait aperture");
                    }
                    else
                    {
                        Check(local.yMin>-270+20&&local.height<1080*.7f,"quest figures are smaller with both feet above the native lower help text");
                        Matrix4x4 previous=Matrix4x4.Scale(Vector3.one*.88f)*rawRect.worldToLocalMatrix*BodyMap730(scene.ModelCamera,ground.transform,rawRect);
                        float oldGround=previous.MultiplyPoint3x4(ground.transform.position).y;
                        previous=Matrix4x4.Translate(Vector3.up*Mathf.Max(0f,-270f+1080f*.03f-oldGround))*previous;
                        File.AppendAllText(Path.Combine(output,"quest-frame753.txt"),"actual="+view.NativeToGraphic+"\n752="+previous+"\n");
                        Check(Same(rawRect.localToWorldMatrix*view.NativeToGraphic,rawRect.localToWorldMatrix*previous),"accepted quest preview keeps its complete752 native-to-graphic matrix");
                    }
                    Texture2D head=Read(scene.HeadTarget,Path.Combine(output,"creation-"+page+"-"+actor+"-head.png"));Rect pixels=CyanBounds752(head);
                    File.WriteAllLines(Path.Combine(output,"native-render-messages.txt"),GloomhavenVR.Core.VRLog.Messages);
                    Matrix4x4 toWorld=rawRect.localToWorldMatrix*view.NativeToGraphic;
                    Rect expected=HeadBox753(scene.Head,toWorld,body.GetComponent<Renderer>().bounds);
                    if(bear.activeSelf){Rect other=HeadBox753(scene.Head,toWorld,bear.GetComponent<Renderer>().bounds);expected=Rect.MinMaxRect(Mathf.Min(expected.xMin,other.xMin),Mathf.Min(expected.yMin,other.yMin),Mathf.Max(expected.xMax,other.xMax),Mathf.Max(expected.yMax,other.yMax));}
                    Vector2 corner=expected.center;
                    File.AppendAllText(Path.Combine(output,"pixel-positions.txt"),page+","+actor+",local="+local+",pixels="+pixels+",expectedCenter="+corner+"\n");
                    Check(Mathf.Abs(pixels.center.x-corner.x)<8&&Mathf.Abs(pixels.center.y-corner.y)<8,"final head pixels follow the live page-specific figure frame without residual flat copy");
                    if(page==1)
                    {
                        Vector3 key=keyboard.transform.position;Vector3 eye=scene.Head.transform.position;
                        bool blocked=MenuDepthSources.TryRaycast(owner.GetComponent<Canvas>(),eye,(key-eye).normalized,10,out _);
                        Check(!blocked,"the creation figure does not intercept the laser aimed at the native keyboard");
                    }
                    Check(ground.transform.position==initialGround&&rawRect.position==rawPosition,"page-specific figure fit preserves gameplay ground and native UI transforms");
                    Object.DestroyImmediate(head);yield return null;
                }
            }
            finally{PanelSupersample.EndHead(entry);Object.DestroyImmediate(material);}
        }
        complete(_count);
    }
}
