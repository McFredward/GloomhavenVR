extern alias native;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
    private static readonly Dictionary<string,TMP_FontAsset> _earlyFonts748=new();
    private static readonly Dictionary<string,Texture2D> _earlyTextures748=new();
    private static readonly List<Object> _earlyAssets748=new();
    private static readonly List<RectTransform> _earlyRoots748=new();
    private static void CleanupEarly748()
    {
        foreach(RectTransform root in _earlyRoots748)if(root!=null)Object.DestroyImmediate(root.gameObject);
        for(int i=_earlyAssets748.Count-1;i>=0;i--)if(_earlyAssets748[i]!=null)Object.DestroyImmediate(_earlyAssets748[i]);
        _earlyAssets748.Clear();_earlyRoots748.Clear();_earlyFonts748.Clear();_earlyTextures748.Clear();
    }

    private static RectTransform NativeNarrative748(string route,Transform home)
    {
        var document=new XmlDocument();document.Load(Path.Combine(_inputs,"native-narrative-"+route+".xml"));
        var root=ImportNode739(document.DocumentElement!,home,_earlyFonts748,true,_earlyTextures748,_earlyAssets748);_earlyRoots748.Add(root);
        root.gameObject.SetActive(true);
        foreach(CanvasGroup group in root.GetComponentsInChildren<CanvasGroup>(true))group.alpha=1;
        return root;
    }

    private static (UIWindow Window,StoryImageViewer Viewer,Image[] Backings) NativeLoadout748(RectTransform host)
    {
        var window=host.gameObject.AddComponent<UIWindow>();
        var info=(RectTransform)host.Find("UI Loadout Quest Information");info.gameObject.SetActive(true);
        var picture=info.Find("Holder/Paper/Image").GetComponent<Image>();
        foreach(Transform node in host.GetComponentsInChildren<Transform>(true))
            if(picture.transform.IsChildOf(node))node.gameObject.SetActive(true);
        var manager=host.gameObject.AddComponent<UILoadoutManager>();manager.questInfo=info.gameObject.AddComponent<UILoadoutQuestWindow>();
        var viewer=info.gameObject.AddComponent<StoryImageViewer>();manager.questInfo.imagePaper=viewer;
        viewer.container=info.gameObject;viewer.imageHolder=picture;
        return(window,viewer,new[]{info.GetComponent<Image>(),info.Find("Blur").GetComponent<Image>(),info.Find("Blur (1)").GetComponent<Image>()});
    }

    public static IEnumerator RunEarlyLoadout748(string output)=>RunLoadout748(output,false);
    public static IEnumerator RunBackdropOwnership748(string output)=>RunLoadout748(output,true);

    private static IEnumerator RunLoadout748(string output,bool ownershipOnly)
    {
        _checks=0;_allowNativeButton=true;NativeFont();
        var harmony=_harmony=new Harmony("GloomhavenVR.EarlyLoadout748."+typeof(LevelUpNativeProgram).Assembly.GetName().Name);
        harmony.Patch(AccessTools.Method(typeof(native::ControllerInputClickeable),"OnEnable"),prefix:new HarmonyMethod(Method(nameof(ControllerEnable))));
        harmony.Patch(AccessTools.Method(typeof(native::ExtendedButton).Assembly.GetType("InteractabilityManager"),"ShouldAllowClickForExtendedButton"),prefix:new HarmonyMethod(Method(nameof(BlockNativeButton))));
        new GameObject("early native pointer",typeof(EventSystem));
        var head=new GameObject("first visible native eye").AddComponent<Camera>();head.enabled=false;
        head.clearFlags=CameraClearFlags.SolidColor;head.backgroundColor=new Color(.12f,.27f,.4f,1);
        head.fieldOfView=55;head.nearClipPlane=.01f;head.farClipPlane=100;head.cullingMask=(1<<5)|(1<<26);
        var target=new RenderTexture(960,640,24);target.Create();head.targetTexture=target;CanvasConversion.WorldCamera=head;
        var home=Rect("native first reveal home",null,1920,1080);home.localScale=Vector3.one*.001f;
        var canvas=home.gameObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.worldCamera=head;
        var art=LoadTexture("native-loadout-illustration");var sprite=Sprite.Create(art,new Rect(0,0,art.width,art.height),new Vector2(.5f,.5f),100);sprite.name="CR_RU_01_Demon";
        _earlyAssets748.Add(art);_earlyAssets748.Add(sprite);
        var rows=new List<string>{"mode,ready,phase,path,view,error"};
        var singleplayerPixels=new Dictionary<string,Color32[]>();
        bool wasOnline=FFSNetwork.IsOnline;
        try
        {
            if(!ownershipOnly)foreach(bool online in new[]{false,true})
            foreach(bool ready in new[]{false,true})
            {
                FFSNetwork.IsOnline=online;string mode=online?"mp":"sp";
                var host=NativeNarrative748("loadout",home);var native=NativeLoadout748(host);
                var picture=native.Viewer.imageHolder;picture.sprite=ready?sprite:null;picture.gameObject.SetActive(ready);
                var flags=new Dictionary<Graphic,bool>();foreach(Graphic graphic in host.GetComponentsInChildren<Graphic>(true))flags.Add(graphic,graphic.enabled);
                var fade=host.GetComponent<CanvasGroup>()??throw new InvalidOperationException("Original window CanvasGroup missing");
                Check(fade.gameObject==host.gameObject,"native first-visible fixture retains the original window CanvasGroup");fade.alpha=0;
                Canvas.ForceUpdateCanvases();
                var filter=NarrativeBackdrop.Filter(native.Window,false);
                var panel=CanvasConversion.Convert(host,"native before story Park",useModLayer:true,transparentBackground:filter!=null,backgroundFilter:filter)!;
                panel.HostRect.SetPositionAndRotation(Vector3.zero,Quaternion.identity);panel.HostRect.localScale=Vector3.one*.001f;
                bool dimmerHiddenAtAlphaZero=!native.Backings[0].enabled,blurHiddenAtAlphaZero=!native.Backings[1].enabled;
                bool inactiveReceiptAtAlphaZero=!native.Backings[2].enabled&&panel.HiddenBackgrounds.Contains(native.Backings[2]);
                try
                {
                    fade.alpha=.836f;Canvas.ForceUpdateCanvases();
                    Check(CanvasConversion.MeasureFirst748(panel,out Vector2 measured,out Vector2 center),"actual first native fit measures the revealing unloaded loadout foreground");
                    Vector2 firstMeasure=measured;
                    CanvasConversion.ApplyFirst748(panel,ref measured,ref center);
                    foreach(float alpha in new[]{.05f,.836f,1f})
                    {
                        fade.alpha=alpha;Canvas.ForceUpdateCanvases();
                        foreach(bool captured in new[]{false,true})
                        using(var capture=captured?new PanelSupersample.ConfirmationCapture(panel):null)
                        for(int view=0;view<4;view++)
                        {
                            head.transform.position=view==0?new Vector3(0,0,-2.1f):view==1?new Vector3(.9f,.25f,-2):view==2?new Vector3(-.8f,-.15f,-2):new Vector3(0,0,2.1f);
                            head.transform.rotation=Quaternion.LookRotation(-head.transform.position,Vector3.up);
                            string label="early-"+mode+"-"+ready+"-"+alpha.ToString("R")+"-"+captured+"-"+view;
                            capture?.Draw();Texture2D actual=Capture(head,Path.Combine(output,label+".png"));
                            string parityKey=ready+"-"+alpha.ToString("R")+"-"+captured+"-"+view;
                            if(!online)singleplayerPixels.Add(parityKey,actual.GetPixels32());
                            else
                            {
                                Color32[] sp=singleplayerPixels[parityKey],mp=actual.GetPixels32();bool same=sp.Length==mp.Length;
                                for(int i=0;same&&i<sp.Length;i++)same=sp[i].r==mp[i].r&&sp[i].g==mp[i].g&&sp[i].b==mp[i].b&&sp[i].a==mp[i].a;
                                Check(same,"first-visible loadout pixels are identical for SP and MP session inputs");
                            }
                            var enabled=new bool[native.Backings.Length];
                            for(int i=0;i<enabled.Length;i++){enabled[i]=native.Backings[i].enabled;native.Backings[i].enabled=false;}
                            capture?.Draw();Texture2D reference=Capture(head,Path.Combine(output,label+"-reference.png"));
                            float error=Difference(actual,reference);rows.Add(mode+","+ready+","+alpha.ToString("R")+","+captured+","+view+","+error.ToString("R"));
                            File.WriteAllLines(Path.Combine(output,"early-loadout-pixels.csv"),rows);
                            for(int i=0;i<enabled.Length;i++)native.Backings[i].enabled=enabled[i];
                            Object.DestroyImmediate(actual);Object.DestroyImmediate(reference);
                            Check(error<.00001f,"first revealing native loadout eye pixels have no desktop dimmer before story Park");
                        }
                    }
                    foreach(Image backing in native.Backings)Check(!backing.enabled,"native converter removes every exact loadout backing before first fit and capture");
                    Check(dimmerHiddenAtAlphaZero&&blurHiddenAtAlphaZero,"exact native backing roles are already disabled at alpha zero before the first fit and draw");
                    Check(inactiveReceiptAtAlphaZero,"native inactive Blur retains its original enabled receipt before capture neutralization");
                    Check(firstMeasure.x<1900,"actual first native fit excludes the full desktop backing even before illustration readiness");
                    foreach(Graphic graphic in flags.Keys)
                        if(Array.IndexOf(native.Backings,graphic)<0)Check(graphic.enabled==flags[graphic],"early conversion preserves native illustration paper input mask and button flags");
                    picture.sprite=sprite;picture.gameObject.SetActive(true);
                    var story=NativeNarrative748("story",home);var storyWindow=story.gameObject.AddComponent<UIWindow>();
                    foreach(Transform node in story.GetComponentsInChildren<Transform>(true))
                        if(node.name=="Container"||node.name=="Dialog"||node.name=="DialogContent")node.gameObject.SetActive(true);
                    Check(NativeStoryComposite746.ParkOriginal(native.Window,host,storyWindow,picture.rectTransform),"actual late story Park adopts the already converted native loadout");
                    NativeStoryComposite746.TickOriginal(host);NativeStoryComposite746.UnparkOriginal();
                    foreach(Image backing in native.Backings)Check(!backing.enabled,"unpark retains converter ownership of the early native host dimmer");
                    Object.DestroyImmediate(story.gameObject);
                }
                finally{NativeStoryComposite746.UnparkOriginal();CanvasConversion.Release(panel);}
                foreach(Graphic graphic in flags.Keys)Check(graphic.enabled==flags[graphic],"converter native hand-back restores exact original loadout graphic flags");
                Object.DestroyImmediate(host.gameObject);
            }
            BackdropOwnership748(home,head,sprite,output);
        }
        finally
        {
            FFSNetwork.IsOnline=wasOnline;Object.DestroyImmediate(home.gameObject);head.targetTexture=null;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(head.gameObject);
        }
        File.WriteAllLines(Path.Combine(output,"early-loadout-pixels.csv"),rows);
        yield return _checks;
    }

    private static void BackdropOwnership748(RectTransform home,Camera head,Sprite illustration,string output)
    {
        foreach(bool destroyParent in new[]{false,true})
        {
            var source=NativeNarrative748("story",home);var window=source.gameObject.AddComponent<UIWindow>();
            var background=source.Find("UI Story Box/UI Story Background").GetComponent<Image>();var blur=background.transform.Find("Blur").GetComponent<Image>();
            var filter=NarrativeBackdrop.Filter(window,true)!;
            var panel=CanvasConversion.Convert(source,"destroyed original narrative component",useModLayer:true,transparentBackground:true,backgroundFilter:filter)!;
            using(var capture=new PanelSupersample.ConfirmationCapture(panel)){capture.Draw();}
            Object.DestroyImmediate(destroyParent?background:blur);
            bool safe=true,owned=true;
            try{owned=filter(blur);}
            catch(MissingReferenceException){safe=false;}
            catch(NullReferenceException){safe=false;}
            Check(safe&&!owned,destroyParent?"destroyed native backdrop Image safely retires the cached Blur role":"destroyed native Blur Image safely retires its cached original role");
            CanvasConversion.Release(panel);Object.DestroyImmediate(source.gameObject);
        }
        {
            var host=NativeNarrative748("loadout",home);var native=NativeLoadout748(host);native.Viewer.imageHolder.sprite=illustration;
            var source=NativeNarrative748("story",home);var window=source.gameObject.AddComponent<UIWindow>();
            var background=source.Find("UI Story Box/UI Story Background").GetComponent<Image>();var blur=background.transform.Find("Blur").GetComponent<Image>();
            var filter=NarrativeBackdrop.Filter(window,true)!;
            var panel=CanvasConversion.Convert(source,"native role callback failure",useModLayer:true,transparentBackground:true,backgroundFilter:filter)!;
            using(var capture=new PanelSupersample.ConfirmationCapture(panel)){capture.Draw();}
            int before=panel.HiddenBackgrounds.Count,reads=0;
            panel.BackgroundFilter=graphic=>++reads==2?throw new InvalidOperationException("injected second-role failure"):filter(graphic);
            bool parked=NativeStoryComposite746.ParkOriginal(native.Window,host,window,native.Viewer.imageHolder.rectTransform);
            Check(!parked&&reads==2&&panel.HiddenBackgrounds.Count==before&&!background.enabled&&!blur.enabled,
                "failed native role adoption preserves every previous converter receipt and original hidden flag");
            panel.BackgroundFilter=filter;CanvasConversion.Release(panel);
            Check(background.enabled&&blur.enabled,"native owner restores original flags after a rejected composite role adoption");
            Object.DestroyImmediate(source.gameObject);Object.DestroyImmediate(host.gameObject);
        }
        foreach(bool online in new[]{false,true})
        foreach(bool releaseFirst in new[]{false,true})
        foreach(bool sourceDisabled in new[]{false,true})
        {
            FFSNetwork.IsOnline=online;
            var host=NativeNarrative748("loadout",home);var native=NativeLoadout748(host);native.Viewer.imageHolder.sprite=illustration;
            var story=NativeNarrative748("story",home);var sourceWindow=story.gameObject.AddComponent<UIWindow>();
            var background=story.Find("UI Story Box/UI Story Background").GetComponent<Image>();var blur=background.transform.Find("Blur").GetComponent<Image>();
            if(sourceDisabled)background.enabled=false;
            Canvas.ForceUpdateCanvases();
            var sourceFilter=NarrativeBackdrop.Filter(sourceWindow,true);
            var sourcePanel=CanvasConversion.Convert(story,"already converted story source",useModLayer:true,transparentBackground:true,backgroundFilter:sourceFilter)!;
            using(var capture=new PanelSupersample.ConfirmationCapture(sourcePanel)){capture.Draw();}
            Check(sourcePanel.HiddenBackgrounds.Contains(blur),"converted source records the original Blur before moving its native branch");
            Check(NativeStoryComposite746.ParkOriginal(native.Window,host,sourceWindow,native.Viewer.imageHolder.rectTransform),"native Park adopts an already converted original story source");
            Check(sourcePanel.HiddenBackgrounds.Count==0,"story dock takes every converted source backdrop receipt before moving native children");
            if(releaseFirst){CanvasConversion.Release(sourcePanel);NativeStoryComposite746.UnparkOriginal();}
            else
            {
                NativeStoryComposite746.UnparkOriginal();
                Check(!background.enabled&&!blur.enabled,"return to a live native story converter keeps its backdrop continuously disabled");
                Check(sourcePanel.HiddenBackgrounds.Contains(blur)&&sourcePanel.HiddenBackgrounds.Contains(background)!=sourceDisabled,
                    "live native source converter regains exactly its original enabled receipts after child return");
                CanvasConversion.Release(sourcePanel);
            }
            Check(background.enabled!=sourceDisabled&&blur.enabled,"converted source and story dock return native backdrop flags in either release order");
            Object.DestroyImmediate(story.gameObject);Object.DestroyImmediate(host.gameObject);
        }
        foreach(bool online in new[]{false,true})
        foreach(bool releaseFirst in new[]{false,true})
        foreach(bool initiallyDisabled in new[]{false,true})
        {
            FFSNetwork.IsOnline=online;
            var host=NativeNarrative748("loadout",home);var native=NativeLoadout748(host);
            native.Viewer.imageHolder.sprite=illustration;
            var story=NativeNarrative748("story",home);var storyWindow=story.gameObject.AddComponent<UIWindow>();
            foreach(Transform node in story.GetComponentsInChildren<Transform>(true))
                if(node.name=="Container"||node.name=="Dialog"||node.name=="DialogContent")node.gameObject.SetActive(true);
            if(initiallyDisabled)foreach(Image backing in native.Backings)backing.enabled=false;
            Check(NativeStoryComposite746.ParkOriginal(native.Window,host,storyWindow,native.Viewer.imageHolder.rectTransform),"actual story Park can acquire originals before conversion admission");
            var filter=NarrativeBackdrop.Filter(native.Window,false);
            var panel=CanvasConversion.Convert(host,"park before conversion",useModLayer:true,transparentBackground:filter!=null,backgroundFilter:filter)!;
            using(var capture=new PanelSupersample.ConfirmationCapture(panel)){capture.Draw();}
            if(releaseFirst){CanvasConversion.Release(panel);NativeStoryComposite746.UnparkOriginal();}
            else
            {
                NativeStoryComposite746.UnparkOriginal();
                foreach(Image backing in native.Backings)Check(!backing.enabled,"composite handoff never re-enables a backing inside its live converted owner");
                if(!initiallyDisabled)foreach(Image backing in native.Backings)Check(panel.HiddenBackgrounds.Contains(backing),"converter receives every exact original receipt from the earlier composite lease");
                CanvasConversion.Release(panel);
            }
            foreach(Image backing in native.Backings)Check(backing.enabled!=initiallyDisabled,"combined lease and converter restore each native original flag exactly once in either order");
            Object.DestroyImmediate(story.gameObject);Object.DestroyImmediate(host.gameObject);
        }
        foreach(string route in new[]{"story","encounter","loadout"})
        foreach(string reuse in new[]{"none","source","parent"})
        {
            var root=NativeNarrative748(route,home);var window=root.gameObject.AddComponent<UIWindow>();
            Image backing;
            if(route=="loadout")
            {
                Object.DestroyImmediate(window);var native=NativeLoadout748(root);window=native.Window;backing=native.Backings[0];
            }
            else if(route=="encounter"){root.gameObject.AddComponent<UIEventPanel>();backing=root.Find("Background").GetComponent<Image>();}
            else backing=root.Find("UI Story Box/UI Story Background").GetComponent<Image>();
            foreach(Transform node in root.GetComponentsInChildren<Transform>(true))
                if(backing.transform.IsChildOf(node))node.gameObject.SetActive(true);
            var blur=backing.transform.Find("Blur").GetComponent<Image>();
            var filter=NarrativeBackdrop.Filter(window,route=="story")!;
            backing.canvasRenderer.cull=true;blur.canvasRenderer.cull=true;
            var panel=CanvasConversion.Convert(root,"native producer ownership",useModLayer:true,transparentBackground:true,backgroundFilter:filter)!;
            Check(panel.HiddenBackgrounds.Contains(backing)&&panel.HiddenBackgrounds.Contains(blur),"exact native producer owns culled backings before the first reveal");
            backing.canvasRenderer.cull=false;blur.canvasRenderer.cull=false;
            using(var capture=new PanelSupersample.ConfirmationCapture(panel)){capture.Draw();}
            var foreign=Rect("UI Story Box",home,1920,1080);
            if(reuse=="source")backing.sprite=illustration;
            if(reuse=="parent")backing.transform.SetParent(foreign,false);
            if(reuse=="none")Check(filter(blur),"owned native Blur remains identifiable after real capture material neutralization");
            CanvasConversion.Release(panel);
            if(reuse=="none")Check(backing.enabled&&blur.enabled,"owned native dimmer and material-neutralized Blur restore on converter hand-back");
            else Check(!backing.enabled&&!blur.enabled,"converted native background receipts never re-enable pooled source or foreign-parent content");
            Object.DestroyImmediate(foreign.gameObject);Object.DestroyImmediate(root.gameObject);
        }
        // Ordinary no-filter conversion keeps its original restoration contract.
        var ordinary=Rect("ordinary native conversion",home,1920,1080);var opaque=ordinary.gameObject.AddComponent<Image>();opaque.color=Color.white;
        var generic=CanvasConversion.Convert(ordinary,"unfiltered original",transparentBackground:true)!;
        Check(!opaque.enabled,"ordinary unfiltered conversion still hides its original opaque desktop backing");
        opaque.sprite=illustration;CanvasConversion.Release(generic);
        Check(opaque.enabled,"ordinary no-filter restoration remains independent of narrative source roles");
        Object.DestroyImmediate(ordinary.gameObject);
    }
}
