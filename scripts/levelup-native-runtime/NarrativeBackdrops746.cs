extern alias native;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;
using GloomhavenVR.WorldUI;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static partial class LevelUpNativeProgram
{
    public static IEnumerator RunNarrative746(string output)
    {
        _checks=0;_allowNativeButton=true;NativeFont();
        var harmony=_harmony=new Harmony("GloomhavenVR.Narrative746."+typeof(LevelUpNativeProgram).Assembly.GetName().Name);
        harmony.Patch(AccessTools.Method(typeof(native::ControllerInputClickeable),"OnEnable"),prefix:new HarmonyMethod(Method(nameof(ControllerEnable))));
        harmony.Patch(AccessTools.Method(typeof(native::ExtendedButton).Assembly.GetType("InteractabilityManager"),"ShouldAllowClickForExtendedButton"),prefix:new HarmonyMethod(Method(nameof(BlockNativeButton))));
        new GameObject("narrative pointer",typeof(EventSystem));
        var head=new GameObject("narrative final eye").AddComponent<Camera>();head.enabled=false;
        head.clearFlags=CameraClearFlags.SolidColor;head.backgroundColor=new Color(.12f,.27f,.4f,1);
        head.orthographic=false;head.fieldOfView=55;head.nearClipPlane=.01f;head.farClipPlane=100;
        head.transform.position=new Vector3(0,0,-2.1f);head.cullingMask=(1<<5)|(1<<26);
        var target=new RenderTexture(960,640,24);target.Create();head.targetTexture=target;CanvasConversion.WorldCamera=head;
        var home=Rect("native narrative home",null,1920,1080);home.localScale=Vector3.one*.001f;
        var rootCanvas=home.gameObject.AddComponent<Canvas>();rootCanvas.renderMode=RenderMode.WorldSpace;rootCanvas.worldCamera=head;
        string[] routes={"story","encounter","quest"};
        foreach(string route in routes)
        {
            var document=new XmlDocument();document.Load(Path.Combine(_inputs,"native-narrative-"+route+".xml"));
            var root=ImportNode739(document.DocumentElement!,home,new Dictionary<string,TMP_FontAsset>(),true);
            root.gameObject.SetActive(true);
            // Completed native reveal/localized content are surrounding inputs;
            // original authored rectangles, sprites, masks and fonts remain.
            foreach(RectTransform rect in root.GetComponentsInChildren<RectTransform>(true))
                if(rect.localScale.sqrMagnitude<.001f)rect.localScale=Vector3.one;
            foreach(TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
                if(string.IsNullOrEmpty(text.text))text.text="Native narrative text";
            Canvas.ForceUpdateCanvases();LayoutRebuilder.ForceRebuildLayoutImmediate(root);Canvas.ForceUpdateCanvases();
            var window=root.gameObject.AddComponent<UIWindow>();
            if(route=="encounter")root.gameObject.AddComponent<UIEventPanel>();
            Func<Graphic,bool>? filter=NarrativeBackdrop.Filter(window,route=="story");
            Image? backdrop=route=="story"?root.Find("UI Story Box/UI Story Background").GetComponent<Image>():route=="encounter"?root.Find("Background").GetComponent<Image>():null;
            Image? blur=backdrop!=null?backdrop.transform.Find("Blur").GetComponent<Image>():null;
            Material? originalBlur=blur!=null?blur.material:null;Color originalBlurColor=blur!=null?blur.color:Color.white;
            Image? picture=route=="story"?root.Find("UI Story Box/UI Story Background/Holder/Paper/Image").GetComponent<Image>():route=="encounter"?root.Find("UI Event Information/Image Container/Image").GetComponent<Image>():null;
            Graphic[] originals=root.GetComponentsInChildren<Graphic>(true);var flags=new Dictionary<Graphic,bool>();
            foreach(Graphic graphic in originals)flags.Add(graphic,graphic.enabled);
            // Original controllers supply translated text and the active page;
            // make the serialized foreground branch ready without their session.
            foreach(Transform node in root.GetComponentsInChildren<Transform>(true))
                if(node.name=="Container"||node.name=="Dialog"||node.name=="DialogContent"||node.name=="UI Event Information")node.gameObject.SetActive(true);
            var panel=CanvasConversion.Convert(root,"native narrative "+route,useModLayer:true,
                transparentBackground:filter!=null,backgroundFilter:filter)!;
            panel.HostRect.SetPositionAndRotation(Vector3.zero,Quaternion.identity);panel.HostRect.localScale=Vector3.one*.001f;
            var rows=new List<string>();
            try
            {
                foreach(bool captured in new[]{false,true})
                {
                    using(var capture=captured?new PanelSupersample.ConfirmationCapture(panel):null)
                    for(int view=0;view<4;view++)
                    {
                        head.transform.position=view==0?new Vector3(0,0,-2.1f):view==1?new Vector3(.9f,.25f,-2):view==2?new Vector3(-.8f,-.15f,-2):new Vector3(0,0,2.1f);
                        head.transform.rotation=Quaternion.LookRotation(-head.transform.position,Vector3.up);
                        capture?.Draw();string label=route+"-"+(captured?"capture":"direct")+"-"+view;
                        Texture2D actual=Capture(head,Path.Combine(output,"narrative-"+label+".png"));
                        bool enabled=backdrop!=null&&backdrop.enabled,blurEnabled=blur!=null&&blur.enabled;
                        if(backdrop!=null)backdrop.enabled=false;if(blur!=null)blur.enabled=false;
                        capture?.Draw();Texture2D reference=Capture(head,Path.Combine(output,"narrative-"+label+"-reference.png"));
                        float error=Difference(actual,reference);rows.Add(label+","+error.ToString("R"));
                        int visible=0;Color32 background=head.backgroundColor;
                        foreach(Color32 pixel in reference.GetPixels32())
                            if(Math.Abs(pixel.r-background.r)+Math.Abs(pixel.g-background.g)+Math.Abs(pixel.b-background.b)>20)visible++;
                        if(backdrop!=null)backdrop.enabled=enabled;if(blur!=null)blur.enabled=blurEnabled;
                        Object.DestroyImmediate(actual);Object.DestroyImmediate(reference);
                        Check(error<.00001f,"native "+route+" "+(captured?"capture":"direct")+" pixels contain no desktop dimmer outside original foreground");
                        Check(visible>150,"original native narrative foreground actually renders in final head pixels");
                        yield return null;
                    }
                }
                File.WriteAllLines(Path.Combine(output,"narrative-"+route+"-pixels.csv"),rows);
                Check((filter==null)==(route=="quest"),"only actual native narrative producers opt in to desktop dimmer suppression");
                foreach(Graphic graphic in originals)
                    if(graphic!=backdrop&&graphic!=blur)
                        Check(graphic.enabled==flags[graphic],"original native narrative foreground and input graphics retain their enabled state");
                if(picture!=null)
                {
                    // A full-frame runtime illustration is still an illustration,
                    // even when it reuses a dark sprite during native loading.
                    Transform pictureHome=picture.transform.parent;Sprite originalPicture=picture.sprite;
                    Vector2 pictureMin=picture.rectTransform.anchorMin,pictureMax=picture.rectTransform.anchorMax,
                        pictureSize=picture.rectTransform.sizeDelta,picturePosition=picture.rectTransform.anchoredPosition;
                    Vector3 pictureScale=picture.rectTransform.localScale;bool pictureActive=picture.gameObject.activeSelf;
                    picture.transform.SetParent(root,false);
                    picture.sprite=backdrop!.sprite;picture.rectTransform.anchorMin=Vector2.zero;picture.rectTransform.anchorMax=Vector2.one;
                    picture.rectTransform.offsetMin=picture.rectTransform.offsetMax=Vector2.zero;picture.rectTransform.localScale=Vector3.one;
                    picture.gameObject.SetActive(true);Canvas.ForceUpdateCanvases();picture.canvasRenderer.cull=false;
                    CanvasConversion.SweepBackgrounds739(panel);
                    Check(picture.enabled,"native full-frame illustrations remain enabled beside hidden desktop dimmers");
                    picture.transform.SetParent(pictureHome,false);
                    picture.sprite=originalPicture;picture.rectTransform.anchorMin=pictureMin;picture.rectTransform.anchorMax=pictureMax;
                    picture.rectTransform.sizeDelta=pictureSize;picture.rectTransform.anchoredPosition=picturePosition;picture.rectTransform.localScale=pictureScale;
                    picture.gameObject.SetActive(pictureActive);
                    var input=(route=="story"?root.Find("UI Story Box"):root).GetComponent<Image>();
                    Check(input.raycastTarget&&input.color.a==0&&input.enabled,"native transparent input roots retain their original raycast component");
                    bool was=backdrop.raycastTarget;backdrop.raycastTarget=true;
                    Check(!filter!(backdrop),"native transparent input roots retain their original raycast component");backdrop.raycastTarget=was;
                    var mask=backdrop.gameObject.AddComponent<Mask>();bool before=blur!.enabled,bgBefore=backdrop.enabled;
                    backdrop.enabled=true;blur.material=originalBlur;blur.color=originalBlurColor;blur.enabled=true;
                    CanvasConversion.SweepBackgrounds739(panel);
                    Check(blur.enabled,"original native narrative stencil remains enabled");blur.enabled=before;backdrop.enabled=bgBefore;Object.DestroyImmediate(mask);
                    var foreign=Rect("foreign native story",null,1920,1080);
                    var foreignBox=Rect("UI Story Box",foreign,1920,1080);
                    var foreignImage=Image(foreignBox,"UI Story Background",1920,1080,Color.white);foreignImage.raycastTarget=false;foreignImage.sprite=backdrop.sprite;
                    Check(!filter!(foreignImage),"foreign native story graphics cannot join another producer background filter");Object.DestroyImmediate(foreign.gameObject);
                    if(route=="story")
                    {
                        var button=input.gameObject.AddComponent<native::ExtendedButton>();button.useAudioController=false;button.textLanguageKey="";button.targetGraphic=input;
                        int clicks=0;button.onClick.AddListener(()=>clicks++);head.transform.SetPositionAndRotation(new Vector3(0,0,-2.1f),Quaternion.identity);
                        var pointer=new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left,position=head.WorldToScreenPoint(input.transform.position)};
                        Canvas.ForceUpdateCanvases();var hits=new List<RaycastResult>();panel.HostRaycaster.Raycast(pointer,hits);
                        bool dispatched=false;
                        foreach(RaycastResult hit in hits)
                            if(hit.gameObject==input.gameObject||hit.gameObject.transform.IsChildOf(input.transform))
                            {ExecuteEvents.ExecuteHierarchy(hit.gameObject,pointer,ExecuteEvents.pointerClickHandler);dispatched=true;break;}
                        Check(dispatched&&clicks==1,"original native story skip button remains raycastable and dispatches once after background removal");
                    }
                }
            }
            finally {CanvasConversion.Release(panel);}
            foreach(Graphic graphic in originals)
                if(graphic!=blur)Check(graphic.enabled==flags[graphic],"flat hand-back restores every original narrative graphic flag");
            if(route=="story")
            {
                var host=Rect("native loadout host input",home,1920,1080);var hostWindow=host.gameObject.AddComponent<UIWindow>();
                var hostPicture=Image(host,"native loadout image input",1280,720,Color.white);
                hostPicture.rectTransform.anchoredPosition=new Vector2(0,160);
                var children=new List<Transform>();foreach(Transform child in root)children.Add(child);
                bool bgBefore=backdrop!.enabled;
                Check(NativeStoryComposite746.ParkOriginal(hostWindow,host,window,hostPicture.rectTransform),"actual story Park accepts the ready native story producer");
                Check(!backdrop.enabled&&backdrop.gameObject.activeInHierarchy,"actual story Park and ApplyPose hide only the native dimmer Image");
                backdrop.enabled=true;NativeStoryComposite746.TickOriginal(host);
                Check(!backdrop.enabled,"native late re-enable is reasserted within the actual composite pose tick");
                Check(picture!.enabled,"composite retains the original distinct native imageHolder");
                NativeStoryComposite746.UnparkOriginal();
                Check(backdrop.enabled==bgBefore,"composite unpark restores every original narrative dimmer flag");
                for(int i=0;i<children.Count;i++)
                    Check(children[i].parent==root&&children[i].GetSiblingIndex()==i,"actual story Unpark returns all original children in native sibling order");

                backdrop.enabled=false;
                Check(NativeStoryComposite746.ParkOriginal(hostWindow,host,window,hostPicture.rectTransform),"native-disabled dimmer can be parked without taking its flag");
                NativeStoryComposite746.UnparkOriginal();
                Check(!backdrop.enabled,"native-disabled dimmer stays disabled after composite hand-back");backdrop.enabled=true;

                Check(NativeStoryComposite746.ParkOriginal(hostWindow,host,window,hostPicture.rectTransform),"native story source can be parked again");
                Sprite nativeBackdrop=backdrop.sprite;backdrop.sprite=hostPicture.sprite;
                NativeStoryComposite746.UnparkOriginal();
                Check(!backdrop.enabled,"a reused original graphic with a different source is no longer owned by the narrative lease");
                backdrop.sprite=nativeBackdrop;backdrop.enabled=true;

                var foreignHome=Rect("foreign pooled native home",home,1920,1080);var foreignBox=Rect("UI Story Box",foreignHome,1920,1080);
                Transform originalParent=backdrop.transform.parent;
                Check(NativeStoryComposite746.ParkOriginal(hostWindow,host,window,hostPicture.rectTransform),"native story source can be parked before foreign reparent");
                backdrop.transform.SetParent(foreignBox,false);NativeStoryComposite746.UnparkOriginal();
                Check(!backdrop.enabled&&backdrop.transform.parent==foreignBox,"foreign-pooled native content is not re-enabled or reparented by an expired backdrop lease");
                backdrop.transform.SetParent(originalParent,false);backdrop.enabled=true;Object.DestroyImmediate(foreignHome.gameObject);

                // Native scene destruction can retire the source while its live
                // children are still in the dock. Actual Unpark must preserve
                // those originals rather than destroy the game-owned subtree.
                Check(NativeStoryComposite746.ParkOriginal(hostWindow,host,window,hostPicture.rectTransform),"native story source can be parked before scene teardown");
                Object.DestroyImmediate(root.gameObject);NativeStoryComposite746.UnparkOriginal();
                Check(backdrop!=null&&backdrop.enabled&&backdrop.gameObject.activeInHierarchy,"source scene loss retires the lease without destroying parked native content");
                Object.DestroyImmediate(host.gameObject);
            }
            if(root!=null)Object.DestroyImmediate(root.gameObject);
        }
        Object.DestroyImmediate(home.gameObject);head.targetTexture=null;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(head.gameObject);
        File.WriteAllText(Path.Combine(output,"narrative-limits.txt"),"Actual role filter, original imported hierarchy/rect/image/TMP/font/sprite/custom native extended-layout declarations, native Black_Backdrop provenance, conversion background kernel, actual StoryComposite Park/WriteLayers/ApplyPose/TryPaintedBounds/Unpark before host conversion, Unity scene geometry and production mono capture/projection/resolve/display kernels execute. Controller classification, native show/localization/readiness and pool/session services are explicit inputs. The composite test uses its actual first-tick no-panel fit path; the converted content-fit branch throws if entered. Non-UI/Default foreground shader programs use UI geometry witnesses; original Windows D3D11 fragments, full native game callbacks, multiplayer and headset appearance are unexecuted. Original transparent input roots and separate imageHolder remain; no GameObject/container/StoryImageViewer is disabled.");
        yield return _checks;
    }
}
