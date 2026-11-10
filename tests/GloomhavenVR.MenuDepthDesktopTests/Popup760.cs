using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Xml;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MenuDepth;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static partial class MenuDepthDesktopProgram
{
    private static float[] Numbers760(XmlElement node,string key)=>Array.ConvertAll(node.GetAttribute(key).Split(','),
        value=>float.Parse(value,CultureInfo.InvariantCulture));
    private static Vector2 Pair760(XmlElement node,string key){float[] p=Numbers760(node,key);return new Vector2(p[0],p[1]);}
    private static Vector3 Triple760(XmlElement node,string key){float[] p=Numbers760(node,key);return new Vector3(p[0],p[1],p[2]);}
    private sealed class OriginalPopup760 : IDisposable
    {
        private readonly string _inputs;
        private readonly List<Object> _resources=new List<Object>();
        private readonly Dictionary<string,TMP_FontAsset> _fonts=new Dictionary<string,TMP_FontAsset>();
        internal OriginalPopup760(string output)=>_inputs=Path.Combine(Path.GetDirectoryName(output)!,"native-popup-inputs");
        private Texture2D Texture(string name)
        {var result=new Texture2D(2,2,TextureFormat.RGBA32,false);result.LoadImage(File.ReadAllBytes(Path.Combine(_inputs,name+".png")));_resources.Add(result);return result;}
        private TMP_FontAsset Font(string prefix)
        {
            if(_fonts.TryGetValue(prefix,out TMP_FontAsset found))return found;
            var font=ScriptableObject.CreateInstance<TMP_FontAsset>();JsonUtility.FromJsonOverwrite(File.ReadAllText(Path.Combine(_inputs,prefix+".json")),font);
            var atlas=Texture(prefix+"-atlas");font.atlasTextures=new[]{atlas};font.atlasPopulationMode=AtlasPopulationMode.Static;
            var material=new Material(Resources.Load<Shader>("OriginalShaders/NativeSDF"));material.mainTexture=atlas;
            var saved=JsonUtility.FromJson<TmpMaterial742>(File.ReadAllText(Path.Combine(_inputs,prefix+"-material.json")));
            material.shaderKeywords=saved.keywords;material.renderQueue=saved.renderQueue;
            foreach(var item in saved.floats)material.SetFloat(item.name,item.value);
            foreach(var item in saved.colors)material.SetColor(item.name,item.value);
            font.material=material;font.ReadFontAssetDefinition();_resources.Add(material);_resources.Add(font);_fonts.Add(prefix,font);return font;
        }
        internal RectTransform Load(Transform parent)
        {var xml=new XmlDocument();xml.Load(Path.Combine(_inputs,"native-confirmation-tree.xml"));return Node(xml.DocumentElement!,parent);}
        private RectTransform Node(XmlElement node,Transform parent)
        {
            var go=new GameObject(node.GetAttribute("name"),typeof(RectTransform));go.SetActive(false);go.layer=parent.gameObject.layer;
            var rect=(RectTransform)go.transform;rect.SetParent(parent,false);
            foreach(XmlElement part in node.SelectNodes("component")!)
            {
                string kind=part.GetAttribute("kind"),json=part.GetAttribute("json");
                if(kind=="RectTransform")
                {
                    rect.anchorMin=Pair760(part,"m_AnchorMin");rect.anchorMax=Pair760(part,"m_AnchorMax");
                    rect.pivot=Pair760(part,"m_Pivot");rect.sizeDelta=Pair760(part,"m_SizeDelta");rect.anchoredPosition=Pair760(part,"m_AnchoredPosition");
                    rect.localPosition=new Vector3(rect.localPosition.x,rect.localPosition.y,Triple760(part,"m_LocalPosition").z);
                    float[] q=Numbers760(part,"m_LocalRotation");rect.localRotation=new Quaternion(q[0],q[1],q[2],q[3]);rect.localScale=Triple760(part,"m_LocalScale");
                }
                else if(kind=="Image")
                {
                    var image=go.AddComponent<Image>();JsonUtility.FromJsonOverwrite(json,image);
                    if(part.HasAttribute("sprite"))
                    {
                        Texture2D texture=Texture(part.GetAttribute("sprite"));float[] pivot=Numbers760(part,"pivot"),border=Numbers760(part,"border");
                        image.sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(pivot[0],pivot[1]),
                            float.Parse(part.GetAttribute("ppu"),CultureInfo.InvariantCulture),0,SpriteMeshType.FullRect,new Vector4(border[0],border[1],border[2],border[3]));
                        _resources.Add(image.sprite);
                    }
                    // The real confirmation foreground lease excludes only the
                    // root dimmer and Blur. Their full-screen presentation is not
                    // the original compact popup's visible foreground.
                    if(node.GetAttribute("name")=="Confirmation Box_pc"||node.GetAttribute("name")=="Blur")image.enabled=false;
                }
                else if(kind=="TextMeshProUGUI")
                {var text=go.AddComponent<TextMeshProUGUI>();JsonUtility.FromJsonOverwrite(json,text);text.font=Font(part.GetAttribute("font"));text.fontSharedMaterial=text.font.material;}
                else
                {
                    Type? type=kind=="VerticalLayoutGroup"?typeof(VerticalLayoutGroup):kind=="HorizontalLayoutGroup"?typeof(HorizontalLayoutGroup)
                        :kind=="ContentSizeFitter"?typeof(ContentSizeFitter):kind=="LayoutElement"?typeof(LayoutElement):null;
                    if(type!=null)JsonUtility.FromJsonOverwrite(json,go.AddComponent(type));
                }
            }
            foreach(XmlElement child in node.SelectNodes("node")!)Node(child,rect);
            go.SetActive(node.GetAttribute("active")=="1");return rect;
        }
        public void Dispose(){for(int i=_resources.Count-1;i>=0;i--)if(_resources[i]!=null)Object.DestroyImmediate(_resources[i]);}
    }
    private static Image Plate760(Transform parent,string name,Vector2 size,Vector2 position,Color color)
    {
        var go=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));go.layer=parent.gameObject.layer;go.transform.SetParent(parent,false);
        Image image=go.GetComponent<Image>();image.color=color;image.rectTransform.sizeDelta=size;image.rectTransform.anchoredPosition=position;return image;
    }
    private static int GlyphPixels760(Texture2D image,Camera head,RectTransform rect)
    {
        var corners=new Vector3[4];rect.GetWorldCorners(corners);float x0=image.width,y0=image.height,x1=0,y1=0;
        foreach(Vector3 corner in corners){Vector3 p=head.WorldToScreenPoint(corner);x0=Mathf.Min(x0,p.x);x1=Mathf.Max(x1,p.x);y0=Mathf.Min(y0,p.y);y1=Mathf.Max(y1,p.y);}
        int count=0;for(int y=Mathf.Max(0,Mathf.FloorToInt(y0));y<Mathf.Min(image.height,Mathf.CeilToInt(y1));y++)
        for(int x=Mathf.Max(0,Mathf.FloorToInt(x0));x<Mathf.Min(image.width,Mathf.CeilToInt(x1));x++)
        {Color p=image.GetPixel(x,y);if(Mathf.Min(p.r,Mathf.Min(p.g,p.b))>.35f&&Mathf.Max(p.r,Mathf.Max(p.g,p.b))-Mathf.Min(p.r,Mathf.Min(p.g,p.b))<.12f)count++;}
        return count;
    }
    private static int NativeGlyphDifference760(Texture2D image,Texture2D muted)
    {
        Color32[] actual=image.GetPixels32(),without=muted.GetPixels32();int changed=0;
        for(int i=0;i<actual.Length;i++)if(Math.Abs(actual[i].r-without[i].r)+Math.Abs(actual[i].g-without[i].g)+Math.Abs(actual[i].b-without[i].b)>10)changed++;
        return changed;
    }
    private static Material CapturedMaterial760(PanelSupersample.Entry entry)=>(Material)typeof(MenuDepthDisplay)
        .GetField("_material",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(entry.MenuDepthDisplay)!;
    private static void Depth760(PanelSupersample.Entry entry,Camera head)
    {
        Vector3 normal=CapturedMaterial760(entry).GetVector("_GvrPlaneNormal");
        Check(MenuDepthProjection.Finite(normal)&&Mathf.Abs(normal.sqrMagnitude-1f)<.0001f&&Vector3.Dot(normal,entry.Panel.HostRect.forward)>.999f,
            "every owned capture uses a finite unit physical plane normal at native pixel scale");
        Vector3 eye=head.transform.position,point=entry.Panel.HostRect.TransformPoint(entry.Frame.center);
        Vector3 ray=point-eye;float along=Vector3.Dot(point-eye,normal)/Vector3.Dot(ray,normal);
        Vector4 clip=GL.GetGPUProjectionMatrix(head.projectionMatrix,true)*head.worldToCameraMatrix*new Vector4(eye.x+ray.x*along,eye.y+ray.y*along,eye.z+ray.z*along,1);
        float depth=clip.z/clip.w;
        Check(!float.IsNaN(depth)&&!float.IsInfinity(depth)&&Mathf.Abs(along-1)<.0001f,
            "the actual shader glass-depth operands reconstruct finite physical window depth");
    }
    private static void NativeHit760(Canvas canvas,RectTransform button,Camera head,EventSystem events,ref int clicks)
    {
        Vector3 point=button.TransformPoint(button.rect.center),origin=head.transform.position,direction=(point-origin).normalized;
        Check(PopupRay760.Test(canvas,origin,direction,head.farClipPlane,out float distance,out Vector3 hit)&&distance>0&&Vector3.Distance(hit,point)<.001f,
            "the original native choice remains on the actual production laser surface");
        var input=new PointerEventData(events){position=RectTransformUtility.WorldToScreenPoint(head,point),button=PointerEventData.InputButton.Left};
        var results=new List<RaycastResult>();canvas.GetComponent<GraphicRaycaster>().Raycast(input,results);bool found=false;
        foreach(var result in results)if(result.gameObject==button.gameObject||result.gameObject.transform.IsChildOf(button)){found=true;break;}
        Check(found,"the actual native GraphicRaycaster reaches the original confirmation choice after production ray intersection");
        int before=clicks;ExecuteEvents.Execute(button.gameObject,input,ExecuteEvents.pointerClickHandler);
        Check(clicks==before+1,"only an explicit original native button press invokes its supplied confirmation callback");
    }

    // Original confirmation layout/sprites/fonts are read-only asset exports.
    // Native gameplay controllers, owner admission and confirmation lease identity
    // are declared inputs; real .6/.7 capture, geometry, eye rendering and uGUI
    // pointer kernels run before/during/after the persistent member's ownership.
    public static IEnumerator HeadPopup760(string output,Action<int> complete)
    {
        _count=0;DesktopEyeInput.Available=true;
        for(int sample=0;sample<4;sample++)
        using(var scene=new Scene())using(var nativeFont=new NativeFont742(output))using(var original=new OriginalPopup760(output))
        {
            float pixelScale=new[]{.0012f,.026f,.009f,.003f}[sample],rig=new[]{1.17f,24.88f,8.92f,2.77f}[sample];
            Quaternion facing=Quaternion.Euler(0,16.6f,0);Vector3 origin=new Vector3(.42f,.17f,-.1f)*rig;
            scene.HideSpatial730();MenuDepthCharacters.Shutdown();scene.Screen.GetComponent<Renderer>().enabled=false;
            foreach(var g in scene.RootA.GetComponentsInChildren<Graphic>(true))g.enabled=false;
            foreach(var g in scene.RootB.GetComponentsInChildren<Graphic>(true))g.enabled=false;
            scene.Ui.Release();scene.Ui.width=824;scene.Ui.height=2160;scene.Ui.Create();
            scene.HeadTarget.Release();scene.HeadTarget.width=800;scene.HeadTarget.height=600;scene.HeadTarget.Create();
            scene.Head.orthographic=false;scene.Head.fieldOfView=65;scene.Head.aspect=4f/3f;
            scene.Head.nearClipPlane=Mathf.Clamp(.05f*rig,.01f,5f);scene.Head.farClipPlane=1000*Mathf.Max(1,rig);
            scene.Head.transform.SetPositionAndRotation(origin,facing);scene.Head.cullingMask=(1<<27)|(1<<25);
            var host=ConfirmationCanvas745("logged fitted ESC760",24,scene.Head,new Vector2(412,1080),origin+facing*Vector3.forward*(1.4f*rig));
            host.rotation=facing;host.localScale=Vector3.one*pixelScale;
            var display=ConfirmationCanvas745("private ESC photograph760",27,scene.Head,host.rect.size,host.position);
            display.rotation=facing;display.localScale=host.localScale;var photo=display.gameObject.AddComponent<RawImage>();photo.texture=scene.Ui;
            scene.OriginalA.cullingMask=1<<24;scene.OriginalA.targetTexture=scene.Ui;scene.OriginalA.enabled=false;
            var entry=PanelSupersample.BindHead(host,scene.OriginalA,scene.Ui,photo);entry.Panel.HostCanvas=host.GetComponent<Canvas>();PanelSupersample.ActualGeometry(entry);
            var target=new GameObject("native full1920 ESC root760",typeof(RectTransform));target.layer=24;target.transform.SetParent(host,false);
            var targetRect=(RectTransform)target.transform;targetRect.sizeDelta=new Vector2(1920,1080);targetRect.anchoredPosition=new Vector2(769,0);entry.Panel.Target=targetRect;
            var column=Plate760(targetRect,"native left368 ESC column760",new Vector2(368,1080),new Vector2(-769,0),new Color(.16f,.1f,.045f));
            var title=nativeFont.Label(column.transform,"ESC title760","Main Menu",new Vector2(350,70),new Vector2(0,430),32);
            var events=new GameObject("native confirmation input760",typeof(EventSystem));
            var member=ConfirmationCanvas745("persistent native confirmation760",23,scene.Head,new Vector2(1920,1080),host.position);
            var imported=original.Load(member);var nativeCanvas=imported.gameObject.AddComponent<Canvas>();nativeCanvas.renderMode=RenderMode.WorldSpace;nativeCanvas.worldCamera=scene.Head;
            imported.gameObject.AddComponent<GraphicRaycaster>();nativeCanvas.sortingOrder=1001;
            foreach(string path in new[]{"Box/Box Content/Swappable Content/Secondary Content","Box/Box Content/Swappable Content/Secondary General Content","Box/Box Content/Report content"})imported.Find(path).gameObject.SetActive(false);
            var question=imported.Find("Box/Box Content/Swappable Content/Primary Content/Text").GetComponent<TMP_Text>();question.text="Are you sure?";
            var yes=(RectTransform)imported.Find("Box/Box Content/Buttons/Yes");var no=(RectTransform)imported.Find("Box/Box Content/Buttons/No");
            int clicks=0;yes.gameObject.AddComponent<Button>().onClick.AddListener(()=>clicks++);no.gameObject.AddComponent<Button>().onClick.AddListener(()=>clicks++);
            var resources=new List<Object>();var worldMaterial=new Material(Shader.Find("Unlit/Color"));worldMaterial.color=new Color(.05f,.12f,.45f);resources.Add(worldMaterial);
            var behind=GameObject.CreatePrimitive(PrimitiveType.Quad);behind.layer=25;behind.name="opaque world BEHIND real ESC760";
            behind.transform.SetPositionAndRotation(host.position+facing*Vector3.forward*(.3f*rig),facing);behind.transform.localScale=new Vector3(3,3,1)*rig;behind.GetComponent<Renderer>().sharedMaterial=worldMaterial;
            var frontMaterial=new Material(Shader.Find("Unlit/Color"));frontMaterial.color=Color.red;resources.Add(frontMaterial);
            var front=GameObject.CreatePrimitive(PrimitiveType.Quad);front.layer=25;front.name="opaque world FRONT partial ESC760";
            front.transform.SetPositionAndRotation(host.position+facing*new Vector3(.15f,.3f,-.15f)*rig,facing);front.transform.localScale=new Vector3(.09f,.1f,1)*rig;front.GetComponent<Renderer>().sharedMaterial=frontMaterial;
            member.gameObject.SetActive(false);PanelSupersample.OriginalMono(entry);
            Canvas.ForceUpdateCanvases();
            ConvertedPanel? owned=null;
            try
            {
                for(int stage=0;stage<5;stage++)
                {
                    bool shown=stage==1||stage==2||stage==4;
                    if(shown&&owned==null)
                    {
                        member.gameObject.SetActive(true);owned=PanelSupersample.MemberPanel(member);owned.Target=imported;ConfirmationOnOwner.Owned=nativeCanvas;
                        PopupPlacement759.Place(entry.Panel,owned,targetRect);PanelSupersample.BindConfirmationMember(entry.Panel,owned);
                    }
                    if(!shown&&owned!=null)
                    {PanelSupersample.ForgetMember(owned);owned=null;ConfirmationOnOwner.Owned=null;member.gameObject.SetActive(false);}
                    if(stage==2){host.localScale=Vector3.one*(pixelScale*1.1f);host.position+=facing*Vector3.right*(.015f*rig);}
                    if(owned!=null)PopupPlacement759.Place(entry.Panel,owned,targetRect);
                    Canvas.ForceUpdateCanvases();LayoutRebuilder.ForceRebuildLayoutImmediate(imported);Canvas.ForceUpdateCanvases();
                    int wait=Time.frameCount;while(Time.frameCount==wait)yield return null;
                    // Ordinary native service precedes batching and the head
                    // draw. Reuse the exact production ServiceEntry caller slice
                    // already bound for creation: geometry -> early retirement
                    // -> visibility, then the eligible automatic mono capture.
                    // Closing is tested in that very first head frame, without
                    // an extra transition render/yield concealing a missing tick.
                    PanelSupersample.CreationLate764(entry);Canvas.ForceUpdateCanvases();
                    if(scene.OriginalA.enabled)scene.OriginalA.Render();
                    for(int eye=0;eye<2;eye++)
                    {
                        foreach(Camera.StereoscopicEye side in new[]{Camera.StereoscopicEye.Left,Camera.StereoscopicEye.Right})
                        {Vector3 at=origin+facing*Vector3.right*((side==Camera.StereoscopicEye.Left?-.032f:.032f)*rig);
                            scene.Head.SetStereoViewMatrix(side,Matrix4x4.Scale(new Vector3(1,1,-1))*Matrix4x4.TRS(at,facing,Vector3.one).inverse);}
                        scene.Render(eye!=0);
                        string prefix=pixelScale.ToString(CultureInfo.InvariantCulture)+"-"+stage+"-"+eye;
                        var pixels=Read(scene.HeadTarget,Path.Combine(output,prefix+".png"));
                        if(shown)
                        {
                            Check(entry.MenuDepthDisplay!=null&&entry.MenuDepthFailures==0,"the actual owned coordinator renders both original surfaces without falling back");
                            Depth760(entry,scene.Head);
                        }
                        if(!shown)
                        {
                            var mono=Read(scene.Ui,Path.Combine(output,prefix+"-mono.png"));Object.DestroyImmediate(mono);
                            File.AppendAllText(Path.Combine(output,"mono760.txt"),prefix+" canvas="+display.GetComponent<Canvas>().enabled+" photo="+photo.isActiveAndEnabled+" cull="+photo.canvasRenderer.cull
                                +" camera="+scene.OriginalA.transform.position.ToString("R")+" near="+scene.OriginalA.nearClipPlane+" far="+scene.OriginalA.farClipPlane+" ortho="+scene.OriginalA.orthographicSize
                                +" mask="+scene.OriginalA.cullingMask+" view="+scene.OriginalA.worldToCameraMatrix+" projection="+scene.OriginalA.projectionMatrix+"\n");
                        }
                        int menu=GlyphPixels760(pixels,scene.Head,title.rectTransform),dialog=0;
                        if(shown)
                        {
                            // Native confirmation type is blue, not white. An
                            // independent render with only that exact original
                            // TMP component muted proves actual glyph ink,
                            // without counting its frame or a matching world.
                            Color questionInk=question.color;question.color=new Color(questionInk.r,questionInk.g,questionInk.b,0);entry.MenuDepthFrame=-1;scene.Render(eye!=0);
                            var muted=Read(scene.HeadTarget,Path.Combine(output,prefix+"-question-muted.png"));
                            dialog=NativeGlyphDifference760(pixels,muted);Object.DestroyImmediate(muted);
                            question.color=questionInk;entry.MenuDepthFrame=-1;scene.Render(eye!=0);
                        }
                        File.AppendAllText(Path.Combine(output,"popup760.csv"),prefix+","+menu+","+dialog+","+host.lossyScale+","+scene.OriginalA.enabled+"\n");
                        Check(menu>15,"the original ESC menu remains visible before during and after owned confirmation at every logged pixel metric");
                        if(shown)
                        {
                            Check(dialog>15,"the complete original confirmation question is visible over opaque world depth in both final eyes");
                            NativeHit760(nativeCanvas,yes,scene.Head,events.GetComponent<EventSystem>(),ref clicks);
                            NativeHit760(nativeCanvas,no,scene.Head,events.GetComponent<EventSystem>(),ref clicks);
                        }
                        else Check(entry.MenuDepthDisplay==null,"closing the original confirmation hands its owner back to the actual mono capture");
                        Vector3 frontPixel=scene.Head.WorldToScreenPoint(front.transform.position);Color occluder=pixels.GetPixel(Mathf.RoundToInt(frontPixel.x),Mathf.RoundToInt(frontPixel.y));
                        Check(occluder.r>.8f&&occluder.g<.1f,"real world geometry in front still occludes the original native window at its physical depth");
                        Object.DestroyImmediate(pixels);
                    }
                }
            }
            finally
            {
                if(owned!=null)PanelSupersample.ForgetMember(owned);ConfirmationOnOwner.Owned=null;PanelSupersample.EndHead(entry);
                Object.DestroyImmediate(member.gameObject);Object.DestroyImmediate(host.gameObject);Object.DestroyImmediate(display.gameObject);Object.DestroyImmediate(events);
                Object.DestroyImmediate(behind);Object.DestroyImmediate(front);foreach(Object resource in resources)Object.DestroyImmediate(resource);
            }
        }
        complete(_count);
    }
}

namespace GloomhavenVR.Hands.Interact
{
    internal static class UguiPokeSurfaces
    {
        internal static IReadOnlyList<Canvas> NestedOf(Canvas canvas)
        {var nested=new List<Canvas>();foreach(Canvas child in canvas.GetComponentsInChildren<Canvas>(true))if(child!=canvas)nested.Add(child);return nested;}
    }
}
