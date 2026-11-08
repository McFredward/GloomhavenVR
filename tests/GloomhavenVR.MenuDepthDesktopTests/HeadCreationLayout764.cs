using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MenuDepth;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static partial class MenuDepthDesktopProgram
{
    [Serializable] public sealed class NativeRect764
    {
        public int nodeId=0,parentId=0,imageType=0,horizontal=1,vertical=512,linked=0;
        public string name="",sprite="",value="",mask="";
        public Vector2 min,max,size,pivot,position,spritePivot;
        public Vector3 scale;public Quaternion rotation;public Color color=Color.white;public Vector4 border;
        public bool image=false,raw=false,text=false,active=true,enabled=true,group=false,window=false,shown=false;
        public bool keyboard=false,classStep=false,questStep=false;
        public float ppu=100,fontSize=24,alpha=1;
    }
    [Serializable] public sealed class NativeTree764 { public string[] records=Array.Empty<string>(); }
    private static Dictionary<int,RectTransform> ImportCreation764(string inputs,Transform parent,NativeFont742 font,
        List<Texture2D> textures,List<Sprite> sprites,RenderTexture model)
    {
        var tree=JsonUtility.FromJson<NativeTree764>(File.ReadAllText(Path.Combine(inputs,"creation764.json")));
        Check(tree.records.Length>200,"native creation export retains the complete authored party/selector hierarchy");
        var result=new Dictionary<int,RectTransform>();var data=new Dictionary<int,NativeRect764>();
        foreach(string line in tree.records)
        {
            var n=JsonUtility.FromJson<NativeRect764>(line);data.Add(n.nodeId,n);
            var go=new GameObject(n.name,typeof(RectTransform));go.layer=24;
            var rect=(RectTransform)go.transform;rect.SetParent(n.parentId==0?parent:result[n.parentId],false);
            rect.anchorMin=n.min;rect.anchorMax=n.max;rect.pivot=n.pivot;rect.sizeDelta=n.size;
            rect.anchoredPosition=n.position;rect.localScale=n.scale;rect.localRotation=n.rotation;
            result.Add(n.nodeId,rect);
            if(n.group)go.AddComponent<CanvasGroup>().alpha=n.alpha;
            if(n.window)go.AddComponent<UIWindow>().IsOpen=n.shown;
            if(n.image)
            {
                var image=go.AddComponent<Image>();image.color=n.color;image.enabled=n.enabled;image.type=(Image.Type)n.imageType;
                if(n.sprite.Length>0)
                {
                    var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);texture.LoadImage(File.ReadAllBytes(Path.Combine(inputs,n.sprite)));textures.Add(texture);
                    var sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),n.spritePivot,n.ppu,0,SpriteMeshType.FullRect,n.border);sprites.Add(sprite);image.sprite=sprite;
                }
            }
            if(n.raw){var image=go.AddComponent<RawImage>();image.color=n.color;image.enabled=n.enabled;image.texture=model;}
            if(n.mask=="RectMask2D")go.AddComponent<RectMask2D>();
            if(n.mask=="Mask")go.AddComponent<Mask>();
            if(n.text)
            {
                var text=go.AddComponent<TextMeshProUGUI>();text.font=font.Font;text.fontSharedMaterial=font.Font.material;
                text.text=n.value;text.fontSize=n.fontSize;text.color=Color.white;
                text.horizontalAlignment=(HorizontalAlignmentOptions)n.horizontal;text.verticalAlignment=(VerticalAlignmentOptions)n.vertical;
                text.enableWordWrapping=true;
            }
        }
        foreach(var pair in result)pair.Value.gameObject.SetActive(data[pair.Key].active);
        foreach(var pair in result)
        {
            var n=data[pair.Key];var go=pair.Value.gameObject;
            if(n.keyboard)go.AddComponent<UIKeyboard>();
            if(n.classStep)go.AddComponent<UICharacterCreatorClassStep>().BindStartingCards(result[n.linked].GetComponent<UIWindow>());
            if(n.questStep)go.AddComponent<UICharacterCreatorPersonalQuestStep>().BindHelp(result[n.linked]);
        }
        return result;
    }
    private static void Open764(RectTransform rect,bool open)
    {
        rect.gameObject.SetActive(true);var window=rect.GetComponent<UIWindow>();if(window!=null)window.IsOpen=open;
        var group=rect.GetComponent<CanvasGroup>();if(group!=null)group.alpha=open?1:0;
    }
    private static Rect InHost764(RectTransform rectangle,RectTransform host)
    {
        var corners=new Vector3[4];rectangle.GetWorldCorners(corners);Vector2 min=Vector2.one*float.PositiveInfinity,max=-min;
        foreach(Vector3 corner in corners){Vector2 point=host.InverseTransformPoint(corner);min=Vector2.Min(min,point);max=Vector2.Max(max,point);}
        return Rect.MinMaxRect(min.x,min.y,max.x,max.y);
    }
    private static void Fits764(RectTransform rectangle,RectTransform host)
    {
        Rect frame=InHost764(rectangle,host);
        Check(frame.xMin>=host.rect.xMin-.5f&&frame.xMax<=host.rect.xMax+.5f
            &&frame.yMin>=host.rect.yMin-.5f&&frame.yMax<=host.rect.yMax+.5f,
            "the complete native creator and RT aperture fit the physical window");
    }
    public static IEnumerator HeadCreationLayout764(string output,Action<int> complete)
    {
        _count=0;DesktopEyeInput.Available=true;
        string inputs=Path.Combine(Path.GetDirectoryName(output)!,"native-creation764-inputs");
        using(var scene=new Scene())using(var font=new NativeFont742(output))
        {
            MenuDepthCharacters.Shutdown();scene.HideSpatial730();
            foreach(var r in new[]{scene.RootA,scene.RootB})foreach(var g in r.GetComponentsInChildren<Graphic>(true))g.enabled=false;
            scene.Screen.GetComponent<Renderer>().enabled=false;
            scene.HeadTarget.Release();scene.HeadTarget.width=1600;scene.HeadTarget.height=1000;scene.HeadTarget.Create();
            scene.Head.orthographic=false;scene.Head.fieldOfView=60;scene.Head.aspect=1.6f;scene.Head.nearClipPlane=.01f;scene.Head.farClipPlane=30;
            scene.Head.cullingMask=1<<27;scene.OriginalA.cullingMask=1<<24;scene.OriginalA.enabled=false;
            var textures=new List<Texture2D>();var sprites=new List<Sprite>();
            var host=ConfirmationCanvas745("native complete party/creation764",24,scene.OriginalA,new Vector2(1988,1080),new Vector3(0,0,2.8f));
            host.localScale=Vector3.one*.00125f;
            var nodes=ImportCreation764(inputs,host,font,textures,sprites,scene.ModelTarget);
            var target=nodes[7924];target.anchorMin=target.anchorMax=Vector2.one*.5f;target.sizeDelta=new Vector2(1920,1080);target.anchoredPosition=Vector2.zero;
            var rail=nodes[11014];var selector=nodes[8177];var nativeCreator=nodes[10672];
            var party=rail.gameObject.AddComponent<NewPartyDisplayUI>();NewPartyDisplayUI.PartyDisplay=party;
            var assembly=selector.gameObject.AddComponent<UICampaignAdventurePartyAssemblyWindow>();party.CharacterSelector=assembly;
            var flow=nodes[8724].gameObject.AddComponent<AdventureCharacterCreator>();assembly.CharacterCreator=flow;
            var creator=nativeCreator.gameObject.AddComponent<UICharacterCreatorWindow>();flow.CreatorWindow=creator;
            var namePage=FindRect760(target,"UI Character Creator Name Step");
            var questPage=nodes[8523];
            creator.BindCreationPages(namePage.GetComponent<UIWindow>(),questPage.GetComponent<UICharacterCreatorPersonalQuestStep>());
            creator.BindClassPage(nodes[8201].GetComponent<UICharacterCreatorClassStep>());
            Open764(rail,true);Open764(selector,true);Open764(nativeCreator,false);
            Open764(nodes[8201],false);Open764(nodes[8523],false);
            // Native selection/population and the original animator endpoints are
            // inputs. No gameplay component is instantiated. The real fixed fit,
            // native target maintenance, character source and final capture run.
            var raw=nodes[11189].GetComponentInChildren<RawImage>(true);raw.enabled=true;raw.color=Color.white;
            Transform holder=scene.ModelCamera.GetComponent<Character3DDisplayManager>().Holder;
            foreach(Transform child in holder)child.gameObject.SetActive(false);
            var ground=new GameObject("native creator ground764");ground.transform.SetParent(holder,false);
            ground.transform.position=scene.ModelCamera.transform.TransformPoint(new Vector3(0,-1.2f,2.56f));
            var body=GameObject.CreatePrimitive(PrimitiveType.Cube);body.layer=8;body.transform.SetParent(ground.transform,false);
            body.transform.localScale=new Vector3(.9f,2.26f,.3f);body.transform.localPosition=new Vector3(0,1.13f,0);
            var material=new Material(Shader.Find("Unlit/Color"));material.color=Color.cyan;body.GetComponent<Renderer>().sharedMaterial=material;
            scene.ModelCamera.GetComponent<Character3DDisplayManager>().BindOriginal(ground.transform);
            var display=ConfirmationCanvas745("complete original party photograph764",27,scene.Head,host.rect.size,host.position);display.localScale=host.localScale;
            var photo=display.gameObject.AddComponent<RawImage>();photo.texture=scene.Ui;
            scene.Ui.Release();scene.Ui.width=4000;scene.Ui.height=1400;scene.Ui.Create();
            scene.OriginalA.targetTexture=scene.Ui;
            scene.OriginalA.ResetProjectionMatrix();scene.OriginalA.ResetWorldToCameraMatrix();
            var entry=PanelSupersample.BindHead(host,scene.OriginalA,scene.Ui,photo);entry.Panel.Target=target;entry.Panel.NativeDesignFrame=new Vector2(1920,1080);
            object fit=CanvasConversion.NewCreationFit764();Vector3 position=host.position;Quaternion rotation=host.rotation;
            Vector3 modelPosition=ground.transform.position;
            try
            {
                for(int episode=0;episode<2;episode++)
                {
                    creator.SetNativeState764(false,false);Open764(nativeCreator,false);Open764(nodes[8201],false);body.SetActive(false);
                    nodes[9518].sizeDelta=new Vector2(-300,0);nodes[11189].anchoredPosition=Vector2.zero;
                    for(int i=0;i<5;i++){Canvas.ForceUpdateCanvases();CanvasConversion.FitCreation764(entry.Panel,fit);yield return null;}
                    Rect initialRail=InHost764(rail,host);
                    creator.SetNativeState764(true,false);Open764(nativeCreator,true);Open764(nodes[8201],true);body.SetActive(true);
                    nodes[11189].anchoredPosition=new Vector2(600,0);
                    // The original prefab already supplies its shared RT. Also
                    // test a delayed texture assignment: geometry has no artwork
                    // readiness dependency even in this stronger schedule.
                    raw.texture=episode==0?null:scene.ModelTarget;
                    for(int step=0;step<6;step++)
                    {
                        nodes[9518].sizeDelta=new Vector2(Mathf.Lerp(-300,-20,step/5f),0);
                        for(int i=0;i<3;i++){Canvas.ForceUpdateCanvases();CanvasConversion.FitCreation764(entry.Panel,fit);yield return null;}
                        Rect frame=InHost764(nativeCreator,host);Vector4 seat=CanvasConversion.CreationSeat764(fit);
                        Check(Mathf.Abs(frame.xMin-seat.w)<1.1f,"the complete native creation frame remains beside the party rail");
                        Check(Mathf.Abs(frame.xMin-InHost764(rail,host).xMax)<30f,"native creation animation never changes the settled party-rail seam");
                        Fits764(nativeCreator,host);Fits764(raw.rectTransform,host);
                        raw.texture=scene.ModelTarget;
                        entry.Frame=host.rect;PanelSupersample.ActualGeometry(entry);display.sizeDelta=host.rect.size;
                        scene.ModelCamera.Render();scene.OriginalA.Render();MenuDepthCharacters.Tick();scene.Eyes(new Vector3(step%2==0?-.03f:.03f,0,0));scene.Render(false);
                        var head=Read(scene.HeadTarget,Path.Combine(output,"creation-"+episode+"-"+step+".png"));
                        Rect cyan=CyanBounds752(head);
                        Check(cyan.xMin>0&&cyan.xMax<scene.HeadTarget.width-1,"the full live creator silhouette reaches final head pixels without edge clipping");
                        Object.DestroyImmediate(head);
                        Check(host.position==position&&host.rotation==rotation&&ground.transform.position==modelPosition,"creation fitting preserves accepted world pose and gameplay model origin");
                        Check((InHost764(rail,host).position-initialRail.position).sqrMagnitude<.1f,"creation never translates the existing party rail");
                    }
                    Vector2 creationSize=host.rect.size;
                    // Native class -> name tween endpoints. Keep one opening
                    // frame through the authored intermediate portrait movement.
                    for(int page=0;page<2;page++)
                    {
                        Open764(nodes[8201],false);Open764(namePage,page==0);Open764(questPage,page==1);Open764(rail,false);
                        nodes[8201].anchoredPosition=new Vector2(-300,0);nodes[9518].sizeDelta=new Vector2(388,0);
                        for(int tween=0;tween<3;tween++)
                        {
                            nodes[11189].anchoredPosition=new Vector2(600*(1-tween/2f),0);
                            Canvas.ForceUpdateCanvases();CanvasConversion.FitCreation764(entry.Panel,fit);yield return null;
                            Fits764(nativeCreator,host);Fits764(raw.rectTransform,host);
                            Check(host.rect.size==creationSize,"native name/quest tweens retain one complete authored creation frame");
                        }
                        entry.Frame=host.rect;PanelSupersample.ActualGeometry(entry);display.sizeDelta=host.rect.size;
                        scene.ModelCamera.Render();scene.OriginalA.Render();MenuDepthCharacters.Tick();scene.Eyes(Vector3.zero);scene.Render(false);
                        var image=Read(scene.HeadTarget,Path.Combine(output,"page-"+episode+"-"+page+".png"));CyanBounds752(image);Object.DestroyImmediate(image);
                        if(page==1){Fits764(nodes[9355],host);Fits764(nodes[11201],host);}
                    }
                    Open764(namePage,false);Open764(questPage,false);Open764(rail,true);Open764(nodes[8201],true);
                    nodes[8201].anchoredPosition=Vector2.zero;nodes[11189].anchoredPosition=new Vector2(600,0);nodes[9518].sizeDelta=new Vector2(-20,0);
                    // Original IsShown stays true during native Hide's fade.
                    creator.SetNativeState764(false,true);
                    for(int fade=0;fade<3;fade++)
                    {
                        nativeCreator.GetComponent<CanvasGroup>().alpha=1f-fade*.4f;
                        Canvas.ForceUpdateCanvases();CanvasConversion.FitCreation764(entry.Panel,fit);yield return null;
                        Check(host.rect.size==creationSize,"native closing fade retains the creator's presentation frame until OnHidden");
                    }
                    creator.SetNativeState764(false,false);Open764(nativeCreator,false);Open764(nodes[8201],false);body.SetActive(false);
                    nodes[9518].sizeDelta=new Vector2(-300,0);nodes[11189].anchoredPosition=Vector2.zero;
                    for(int i=0;i<5;i++){Canvas.ForceUpdateCanvases();CanvasConversion.FitCreation764(entry.Panel,fit);yield return null;}
                    Vector4 cancelled=CanvasConversion.CreationSeat764(fit);
                    Check(cancelled.x<1.1f&&cancelled.y<1.1f,"cancel returns the native mercenary list to the unchanged party-rail seam without a gap");
                    entry.Frame=host.rect;PanelSupersample.ActualGeometry(entry);display.sizeDelta=host.rect.size;
                    scene.ModelCamera.Render();scene.OriginalA.Render();MenuDepthCharacters.Tick();scene.Eyes(Vector3.zero);scene.Render(false);
                    Object.DestroyImmediate(Read(scene.Ui,Path.Combine(output,"cancel-native-"+episode+".png")));
                    File.WriteAllText(Path.Combine(output,"cancel-state-"+episode+".txt"),
                        "camera="+scene.OriginalA.transform.localToWorldMatrix+"\nview="+scene.OriginalA.worldToCameraMatrix
                        +"\nprojection="+scene.OriginalA.projectionMatrix+"\ntarget="+scene.OriginalA.targetTexture
                        +"\nphoto="+photo.enabled+","+photo.GetComponent<Canvas>().enabled+","+photo.texture
                        +"\nrail="+rail.GetComponent<CanvasGroup>().alpha+","+rail.gameObject.activeInHierarchy
                        +"\nmessages="+string.Join("\n",GloomhavenVR.Core.VRLog.Messages));
                    var cancelledPixels=Read(scene.HeadTarget,Path.Combine(output,"cancel-"+episode+".png"));
                    int painted=0;foreach(var pixel in cancelledPixels.GetPixels32())if(pixel.r+pixel.g+pixel.b>100)painted++;
                    Check(painted>3000,"cancel retains the native party rail and mercenary list in final head pixels");Object.DestroyImmediate(cancelledPixels);
                    // User resize is a world-scale input, never owned by fitting.
                    host.localScale*=1.1f;display.localScale=host.localScale;Vector3 resized=host.localScale;
                    for(int i=0;i<3;i++)CanvasConversion.FitCreation764(entry.Panel,fit);
                    Check(host.localScale==resized,"creation/cancel fitting never resets the user's accepted resize");
                }
            }
            finally
            {
                PanelSupersample.EndHead(entry);NewPartyDisplayUI.PartyDisplay=null;Object.DestroyImmediate(host.gameObject);Object.DestroyImmediate(display.gameObject);
                foreach(var sprite in sprites)Object.DestroyImmediate(sprite);foreach(var texture in textures)Object.DestroyImmediate(texture);Object.DestroyImmediate(material);
            }
        }
        complete(_count);
    }
}
