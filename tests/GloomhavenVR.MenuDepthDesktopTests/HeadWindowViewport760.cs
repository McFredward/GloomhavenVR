using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static partial class MenuDepthDesktopProgram
{
    [Serializable] public sealed class NativeRect760
    {
        public int nodeId=0,parentId=0;public string name="",sprite="",value="";
        public Vector2 min,max,size,pivot,position,spritePivot;
        public Vector3 scale;public Quaternion rotation;public Color color=Color.white;public Vector4 border;
        public bool image=false,text=false,active=true;public float ppu=100,fontSize=24;
    }
    [Serializable] public sealed class NativeTree760 { public string[] records=Array.Empty<string>();public int rootId=0; }
    private static RectTransform ImportTree760(NativeTree760 tree,Transform parent,string inputs,NativeFont742 font,List<Texture2D> textures,List<Sprite> sprites)
    {
        Check(tree.records.Length>100,"original native creation export includes every authored hierarchy node");
        var nodes=new Dictionary<int,RectTransform>();
        foreach(var json in tree.records){var data=JsonUtility.FromJson<NativeRect760>(json);nodes.Add(data.nodeId,ImportRect760(data,data.parentId==0?parent:nodes[data.parentId],inputs,font,textures,sprites));}
        foreach(var json in tree.records){var data=JsonUtility.FromJson<NativeRect760>(json);nodes[data.nodeId].gameObject.SetActive(data.active);}
        return nodes[tree.rootId];
    }
    private static RectTransform ImportRect760(NativeRect760 data,Transform parent,string inputs,NativeFont742 font,List<Texture2D> textures,List<Sprite> sprites)
    {
        var go=new GameObject(data.name,typeof(RectTransform));go.layer=24;
        var rect=(RectTransform)go.transform;rect.SetParent(parent,false);rect.anchorMin=data.min;rect.anchorMax=data.max;
        rect.pivot=data.pivot;rect.sizeDelta=data.size;rect.anchoredPosition=data.position;rect.localScale=data.scale;rect.localRotation=data.rotation;
        if(data.image)
        {
            var image=go.AddComponent<Image>();image.color=data.color;image.raycastTarget=true;
            if(data.sprite.Length>0)
            {
                var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);texture.LoadImage(File.ReadAllBytes(Path.Combine(inputs,data.sprite)));textures.Add(texture);
                var sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),data.spritePivot,data.ppu,0,SpriteMeshType.FullRect,data.border);sprites.Add(sprite);
                image.sprite=sprite;image.type=data.border!=Vector4.zero?Image.Type.Sliced:Image.Type.Simple;
            }
        }
        if(data.text)
        {
            var label=font.Label(rect,"original font dynamic content",data.value,new Vector2(420,80),Vector2.zero,data.fontSize);
            label.enableWordWrapping=true;
        }
        return rect;
    }
    private static RectTransform FindRect760(RectTransform root,string name)
    {foreach(var rect in root.GetComponentsInChildren<RectTransform>(true))if(rect.name==name)return rect;throw new Exception("Missing native rect "+name);}
    public static IEnumerator HeadWindowViewport760(string output,Action<int> complete)
    {
        _count=0;DesktopEyeInput.Available=true;
        string inputs=Path.Combine(Path.GetDirectoryName(output)!,"native-creation-inputs");
        var native=JsonUtility.FromJson<NativeTree760>(File.ReadAllText(Path.Combine(inputs,"creation.json")));
        using(var scene=new Scene())using(var font=new NativeFont742(output))
        {
            scene.HideSpatial730();foreach(var root in new[]{scene.RootA,scene.RootB})foreach(var g in root.GetComponentsInChildren<Graphic>(true))g.enabled=false;
            scene.Screen.GetComponent<Renderer>().enabled=false;scene.HeadTarget.Release();scene.HeadTarget.width=1600;scene.HeadTarget.height=1000;scene.HeadTarget.Create();
            scene.Head.orthographic=false;scene.Head.fieldOfView=60;scene.Head.aspect=1.6f;scene.Head.nearClipPlane=.01f;scene.Head.farClipPlane=20;scene.Head.cullingMask=1<<27;
            scene.OriginalA.enabled=false;scene.OriginalA.cullingMask=1<<24;
            var textures=new List<Texture2D>();var sprites=new List<Sprite>();
            var eventSystem=new GameObject("native EventSystem760",typeof(EventSystem)).GetComponent<EventSystem>();
            // Desktop/root Canvas rectangles are independently observed inputs from
            // both759 logs. Conversion frame selection and the complete capture/
            // final-head coordinator execute current production below.
            foreach(Vector2 desktop in new[]{new Vector2(3440,1440),new Vector2(1920,1080)})
            {
                scene.Ui.Release();scene.Ui.width=(int)desktop.x;scene.Ui.height=(int)desktop.y;scene.Ui.Create();
                var nativeRoot=ConfirmationCanvas745("native root Canvas760",24,scene.OriginalA,new Vector2(desktop.x/desktop.y*1080,1080),Vector3.zero);
                nativeRoot.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceCamera;
                nativeRoot.GetComponent<Canvas>().planeDistance=1f;
                scene.OriginalA.targetTexture=scene.Ui;
                var scaler=nativeRoot.gameObject.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.enabled=false;nativeRoot.GetComponent<Canvas>().scaleFactor=desktop.y/1080f;
                var fixedPopup=new GameObject("coincidentally root-sized fixed popup",typeof(RectTransform),typeof(UIWindow));
                fixedPopup.transform.SetParent(nativeRoot,false);var fixedRect=(RectTransform)fixedPopup.transform;fixedRect.anchorMin=fixedRect.anchorMax=new Vector2(.5f,.5f);fixedRect.sizeDelta=nativeRoot.rect.size;
                Check(NativeWindowFrame.Resolve(fixedRect)==Vector2.zero,"a fixed-size native popup remains authored even when its bounds equal the desktop canvas");
                fixedRect.anchorMin=Vector2.zero;fixedRect.anchorMax=Vector2.one;fixedRect.sizeDelta=new Vector2(-32,-16);
                Check(NativeWindowFrame.Resolve(fixedRect)==new Vector2(1888,1064),"full-screen native frames preserve their authored stretch insets");
                nativeRoot.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;
                Check(NativeWindowFrame.Resolve(fixedRect)==Vector2.zero,"native world-space canvases never acquire desktop frame normalization");
                nativeRoot.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceCamera;
                Object.DestroyImmediate(fixedPopup);
                var target=ImportTree760(native,nativeRoot,inputs,font,textures,sprites);target.gameObject.AddComponent<UIWindow>();foreach(var rect in target.GetComponentsInChildren<RectTransform>(true))if(rect.name=="New Party display"||rect.name=="Campaign Adventure Party Assembly Variant"||rect.name=="UI Adventure Assembly Character Creator"||rect.name=="UI Character Creator Window"||rect.name=="UI Character Creator Personal Quest Step"||rect.name.StartsWith("UI Character Create Personal Quest Choice"))rect.gameObject.SetActive(true);
                Canvas.ForceUpdateCanvases();
                var host=ConfirmationCanvas745("canonical native window760",24,scene.Head,new Vector2(1988,1080),new Vector3(0,0,2.8f));host.localScale=Vector3.one*.0014f;
                Vector2 frame=NativeFrameConversion760.Convert(target,host);
                var left=FindRect760(target,"UI Character Create Personal Quest Choice (1)");var right=FindRect760(target,"UI Character Create Personal Quest Choice");
                Canvas.ForceUpdateCanvases();
                foreach(var quest in new[]{left,right}){Vector3[] corners=new Vector3[4];quest.GetWorldCorners(corners);foreach(var corner in corners){Vector3 point=host.InverseTransformPoint(corner);Check(host.rect.Contains(new Vector2(point.x,point.y)),"both original opposite-edge quest cards fit fully inside the same physical window");}}
                var display=ConfirmationCanvas745("canonical native photograph760",27,scene.Head,host.rect.size,host.position);display.localScale=host.localScale;
                var photograph=display.gameObject.AddComponent<RawImage>();photograph.texture=scene.Ui;
                var entry=PanelSupersample.BindHead(host,scene.OriginalA,scene.Ui,photograph);
                scene.OriginalA.targetTexture=scene.Ui;scene.OriginalA.orthographic=true;scene.OriginalA.aspect=host.rect.width/host.rect.height;scene.OriginalA.orthographicSize=host.rect.height*host.localScale.y*.5f;
                scene.OriginalA.transform.SetPositionAndRotation(host.position-Vector3.forward*2,Quaternion.identity);scene.OriginalA.ResetProjectionMatrix();
                Canvas.ForceUpdateCanvases();scene.OriginalA.Render();scene.Eyes(Vector3.zero);scene.Render(false);yield return null;
                Vector3 acceptedPosition=host.position;Vector3 acceptedScale=host.localScale;
                for(int transition=0;transition<3;transition++)
                {
                    // A live native desktop/window-mode change has no authority over
                    // a window already admitted into its physical design frame.
                    nativeRoot.sizeDelta=transition==1?new Vector2(2580,1080):new Vector2(1920,1080);
                    target.sizeDelta=nativeRoot.sizeDelta;NativeFrameConversion760.Maintain(target,frame);Canvas.ForceUpdateCanvases();
                    Check(host.position==acceptedPosition&&host.localScale==acceptedScale,"desktop changes preserve accepted physical window pose and scale");
                    foreach(var quest in new[]{left,right})
                    {
                        Vector3[] corners=new Vector3[4];quest.GetWorldCorners(corners);
                        foreach(var corner in corners)
                        {Vector3 local=host.InverseTransformPoint(corner);Check(host.rect.Contains(new Vector2(local.x,local.y)),"both original opposite-edge quest cards fit fully inside the same physical window");}
                        var image=quest.GetComponentInChildren<Image>();
                        Vector3 point=image.rectTransform.TransformPoint(image.rectTransform.rect.center);
                        var data=new PointerEventData(eventSystem){position=scene.Head.WorldToScreenPoint(point)};var hits=new List<RaycastResult>();host.GetComponent<GraphicRaycaster>().Raycast(data,hits);
                        Check(hits.Exists(h=>h.gameObject==image.gameObject),"native quest graphics retain direct laser raycast at their physical position");
                    }
                    scene.OriginalA.Render();scene.Eyes(Vector3.zero);scene.Render(false);
                    var pixels=Read(scene.HeadTarget,Path.Combine(output,"desktop-"+desktop.x+"-phase-"+transition+"-left.png"));
                    foreach(var quest in new[]{left,right})
                    {
                        Vector3 center=scene.Head.WorldToScreenPoint(quest.TransformPoint(quest.rect.center));
                        int bright=0;for(int y=-30;y<=30;y++)for(int x=-30;x<=30;x++){Color c=pixels.GetPixel((int)center.x+x,(int)center.y+y);if(c.r>.2f&&c.g>.15f)bright++;}
                        Check(bright>1200,"both original quest parchment surfaces survive in actual final head pixels");
                    }
                    Object.DestroyImmediate(pixels);
                    scene.Render(true);var rightPixels=Read(scene.HeadTarget,Path.Combine(output,"desktop-"+desktop.x+"-phase-"+transition+"-right.png"));Object.DestroyImmediate(rightPixels);
                }
                Check(frame==new Vector2(1920,1080),"desktop aspect never changes the native authored window frame");
                PanelSupersample.EndHead(entry);Object.DestroyImmediate(display.gameObject);Object.DestroyImmediate(host.gameObject);Object.DestroyImmediate(nativeRoot.gameObject);
            }
            foreach(var sprite in sprites)Object.DestroyImmediate(sprite);foreach(var texture in textures)Object.DestroyImmediate(texture);
        }
        complete(_count);
    }
}
