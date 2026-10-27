extern alias native;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GloomhavenVR.WorldUI;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static class LevelUpNativeProgram
{
    private static int _checks,_controllerEnables,_translations,_nativeButtonChecks;
    private static string _language="Continue";
    private static Harmony? _harmony;
    private static TMP_FontAsset _nativeFont=null!;
    [Serializable] public sealed class SavedFloat { public string name="";public float value=0; }
    [Serializable] public sealed class SavedColor { public string name="";public Color value=Color.white; }
    [Serializable] public sealed class SavedMaterial
    { public string[] keywords=Array.Empty<string>();public int renderQueue=0;public SavedFloat[] floats=Array.Empty<SavedFloat>();public SavedColor[] colors=Array.Empty<SavedColor>(); }
    private static string _inputs="";
    public static void Inputs(string path)=>_inputs=path;
    public static void Cleanup()
    {
        AnnouncementContinue.Tick(false);ConfirmationOnOwner.Reset();LevelUpComposite.Reset();
        _harmony?.UnpatchSelf();_harmony=null;CanvasConversion.ActivePanels.Clear();
        foreach(var go in Object.FindObjectsOfType<GameObject>())Object.DestroyImmediate(go);
    }
    private static Texture2D LoadTexture(string name)
    {var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);texture.LoadImage(File.ReadAllBytes(Path.Combine(_inputs,name+".png")));return texture;}
    private static Sprite NativeSprite(string name)
    {var texture=LoadTexture(name);return Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect,new Vector4(13,13,13,13));}
    private static void NativeFont()
    {
        var settings=typeof(TMP_Settings).GetField("s_Instance",BindingFlags.Static|BindingFlags.NonPublic)!;
        if(settings.GetValue(null)==null)settings.SetValue(null,ScriptableObject.CreateInstance<TMP_Settings>());
        _nativeFont=ScriptableObject.CreateInstance<TMP_FontAsset>();
        JsonUtility.FromJsonOverwrite(File.ReadAllText(Path.Combine(_inputs,"font.json")),_nativeFont);
        var atlas=LoadTexture("font-atlas");_nativeFont.atlasTextures=new[]{atlas};_nativeFont.atlasPopulationMode=AtlasPopulationMode.Static;
        var material=new Material(Resources.Load<Shader>("OriginalShaders/NativeSDF"));material.mainTexture=atlas;
        var saved=JsonUtility.FromJson<SavedMaterial>(File.ReadAllText(Path.Combine(_inputs,"font-material.json")));
        material.shaderKeywords=saved.keywords;material.renderQueue=saved.renderQueue;
        foreach(var item in saved.floats)material.SetFloat(item.name,item.value);
        foreach(var item in saved.colors)material.SetColor(item.name,item.value);
        _nativeFont.material=material;_nativeFont.ReadFontAssetDefinition();
    }
    private static void Check(bool value,string message){_checks++;if(!value)throw new Exception(message);}
    private static bool Translate(string Term,ref string __result){_translations++;__result=Term=="GUI_CONTINUE"?_language:Term;return false;}
    private static bool ControllerEnable(){_controllerEnables++;return false;}
    private static bool BlockNativeButton(ref bool __result){_nativeButtonChecks++;__result=false;return false;}
    private static MethodInfo Method(string name)=>typeof(LevelUpNativeProgram).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic)!;
    private static RectTransform Rect(string name,Transform? parent,float width,float height)
    {
        var node=new GameObject(name,typeof(RectTransform));var rect=(RectTransform)node.transform;rect.SetParent(parent,false);
        rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);rect.sizeDelta=new Vector2(width,height);node.layer=5;return rect;
    }
    private static Image Image(RectTransform parent,string name,float width,float height,Color color)
    {var rect=Rect(name,parent,width,height);var image=rect.gameObject.AddComponent<Image>();image.color=color;return image;}
    private static TextMeshProUGUI Text(RectTransform parent,string name,string value,float width,float height)
    {
        var rect=Rect(name,parent,width,height);var text=rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font=_nativeFont;text.fontSharedMaterial=_nativeFont.material;text.text=value;text.fontSize=24;text.color=Color.white;text.enableWordWrapping=false;
        text.overflowMode=TextOverflowModes.Overflow;text.raycastTarget=false;return text;
    }
    private static Texture2D Capture(Camera camera,string filename)
    {
        Canvas.ForceUpdateCanvases();camera.Render();RenderTexture previous=RenderTexture.active;RenderTexture.active=camera.targetTexture;
        var result=new Texture2D(camera.targetTexture.width,camera.targetTexture.height,TextureFormat.RGBA32,false);
        result.ReadPixels(new Rect(0,0,result.width,result.height),0,0);result.Apply();RenderTexture.active=previous;
        File.WriteAllBytes(filename,result.EncodeToPNG());return result;
    }
    private static int BrightPixels(Texture2D image)
    {int count=0;foreach(Color32 p in image.GetPixels32())if(p.r>60&&p.g>60&&p.b>60&&Math.Abs(p.r-p.g)<5&&Math.Abs(p.g-p.b)<5)count++;return count;}
    private static Vector2 GreenCenter(Texture2D image)
    {
        double x=0,y=0;int count=0;Color32[] pixels=image.GetPixels32();
        for(int i=0;i<pixels.Length;i++){var p=pixels[i];if(p.g<100||p.g<p.r*2||p.g<p.b*2)continue;count++;x+=i%image.width+.5;y+=i/image.width+.5;}
        Check(count>300,"actual persistent confirmation remains visible in original Unity UI pixels");return new Vector2((float)(x/count/image.width),(float)(y/count/image.height));
    }
    private static float Difference(Texture2D a,Texture2D b)
    {Color32[] x=a.GetPixels32(),y=b.GetPixels32();long sum=0;for(int i=0;i<x.Length;i++)sum+=Math.Abs(x[i].r-y[i].r)+Math.Abs(x[i].g-y[i].g)+Math.Abs(x[i].b-y[i].b);return sum/(float)(x.Length*3);}
    private static void Record(UIConfirmationBoxManager manager,Component source)
    {var previous=ConfirmationOnOwner.Begin(source);ConfirmationOnOwner.Record(manager);ConfirmationOnOwner.CurrentScope=previous;}
    public static IEnumerator Run(string output)
    {
        _checks=_controllerEnables=_translations=_nativeButtonChecks=0;_language="Continue";
        WorldUIConfig.ConversionActive=true;FlatScreen.ManualScreenActive=false;FFSNetwork.IsOnline=false;
        UIConfirmationBoxManager.MainMenuInstance=null;CanvasConversion.Converts=CanvasConversion.PoseAuthors=0;NativeFont();
        var harmony=_harmony=new Harmony("GloomhavenVR.LevelUpNative."+typeof(LevelUpNativeProgram).Assembly.GetName().Name);
        Assembly game=typeof(native::ExtendedButton).Assembly;
        harmony.Patch(AccessTools.Method(game.GetType("GLOOM.LocalizationManager"),"GetTranslation"),prefix:new HarmonyMethod(Method(nameof(Translate))));
        harmony.Patch(AccessTools.Method(typeof(native::ControllerInputClickeable),"OnEnable"),prefix:new HarmonyMethod(Method(nameof(ControllerEnable))));
        harmony.Patch(AccessTools.Method(game.GetType("InteractabilityManager"),"ShouldAllowClickForExtendedButton"),prefix:new HarmonyMethod(Method(nameof(BlockNativeButton))));
        var scene=SceneManager.CreateScene("level-up owner "+Guid.NewGuid().ToString("N"));
        var root=Rect("native party canvas",null,1988,1080);SceneManager.MoveGameObjectToScene(root.gameObject,scene);
        root.localScale=Vector3.one*.001f;var canvas=root.gameObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;
        var camera=new GameObject("physical test eye").AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=1f;
        camera.transform.position=new Vector3(0,0,-3);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.cullingMask=1<<5;
        var target=new RenderTexture(1000,720,24);target.Create();camera.targetTexture=target;canvas.worldCamera=camera;CanvasConversion.WorldCamera=camera;
        new GameObject("pointer event source",typeof(EventSystem));
        var party=root.gameObject.AddComponent<NewPartyDisplayUI>();party.window=root.gameObject.AddComponent<UIWindow>();party.window.ID=UIWindowID.PartyPanel;NewPartyDisplayUI.PartyDisplay=party;
        var level=Rect("actual level-up root",root,1620,1080);var owner=level.gameObject.AddComponent<UILevelUpWindow>();owner.myWindow=level.gameObject.AddComponent<UIWindow>();owner.myWindow.ID=UIWindowID.HeroLevelUpPanel;
        owner.nextCardTracker=level.gameObject.AddComponent<NativeTracker>();owner.nextCardTracker.OnClick=()=>owner.enableTracker=false;
        Singleton<UILevelUpWindow>.Instance=owner;
        var highlight=Rect("native card highlight",level,325.1f,449.5f);owner.cardHolder=highlight.gameObject.AddComponent<UILevelUpCardHighlighter>();
        var holder=Rect("native card holder",highlight,325.1f,449.5f);owner.cardHolder.cardHolder=holder.gameObject.AddComponent<UILevelUpCardHolder>();
        var card=Rect("original ability card",holder,325.1f,449.5f);owner.cardHolder.cardHolder.Card=card.gameObject.AddComponent<NativeAbilityCard>();
        owner.cardHolder.cardHolder.Card.fullAbilityCard=card.gameObject.AddComponent<NativeFullCard>();
        var body=Text(card,"original card body","Consume an element: gain additional damage and range",220,100);body.fontSize=26;body.alignment=TextAlignmentOptions.Left;body.rectTransform.anchoredPosition=new Vector2(-110,0);
        body.ForceMeshUpdate();
        File.WriteAllText(Path.Combine(output,"text-binding.txt"),"characters="+body.textInfo.characterCount+" vertices="+body.textInfo.meshInfo[0].vertexCount+" bounds="+body.textBounds+" material="+body.fontSharedMaterial.shader.name+" color="+body.color+" font="+body.font.faceInfo.familyName);
        var inventory=Rect("native candidate inventory",root,418,1080);owner.inventory=inventory.gameObject.AddComponent<UILevelUpCardInventory>();inventory.gameObject.AddComponent<UIWindow>();
        var inventoryInk=Image(inventory,"original candidate column ink",418,1080,new Color(.35f,.06f,.04f));
        var comparison=Rect("native comparison deck",root,722,1080);party.AbilityCardsDisplay=comparison.gameObject.AddComponent<NativeCardsDisplay>();
        var comparisonInk=Image(comparison,"original owned column ink",722,1080,new Color(.1f,.1f,.3f));
        var panel=new ConvertedPanel {Target=root,HostRect=root,HostGo=root.gameObject,HostCanvas=canvas,HostRaycaster=root.gameObject.AddComponent<GraphicRaycaster>()};CanvasConversion.ActivePanels.Add(panel);
        // Only the existing frame service is supplied: the new ExtraFrameWidth and actual
        // text/list seating execute production. Global fixed-fit call/pose wiring is source-bound.
        LevelUpComposite.Tick();root.sizeDelta=new Vector2(1988+LevelUpComposite.ExtraFrameWidth(panel),1080);
        var beforeScale=body.rectTransform.lossyScale;
        inventoryInk.enabled=comparisonInk.enabled=false;Texture2D reference=Capture(camera,Path.Combine(output,"native-full-text.png"));
        inventoryInk.enabled=comparisonInk.enabled=true;Texture2D seated=Capture(camera,Path.Combine(output,"three-columns.png"));
        Check(BrightPixels(reference)>20,"original game font produces actual readable reference glyph pixels");
        Check(BrightPixels(seated)>=BrightPixels(reference)-2,"complete original card text remains readable between both native columns in actual pixels");
        Check(body.rectTransform.lossyScale==beforeScale,"level-up layout never shrinks native glyphs to hide clipping");
        Vector3 textRight=body.transform.TransformPoint(body.textBounds.max);var corners=new Vector3[4];inventory.GetWorldCorners(corners);
        Check(level.InverseTransformPoint(corners[0]).x>level.InverseTransformPoint(textRight).x+30,"full text ink rather than nominal card rect determines candidate column clearance");
        Check(root.rect.width==2500&&root.position==Vector3.zero,"level-up gets a wider common native frame without changing accepted host origin");
        Object.DestroyImmediate(reference);Object.DestroyImmediate(seated);
        for(int i=0;i<8;i++)LevelUpComposite.Tick();
        // Same pooled FullAbilityCard, later native language/layout rebuild.
        body.gameObject.SetActive(false);
        body=Text(card,"replacement localized original body","WWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWW",220,100);
        body.fontSize=28;body.alignment=TextAlignmentOptions.Left;body.rectTransform.anchoredPosition=new Vector2(-110,0);body.ForceMeshUpdate();
        LevelUpComposite.Tick();inventory.GetWorldCorners(corners);
        Check(level.InverseTransformPoint(corners[0]).x>level.InverseTransformPoint(body.transform.TransformPoint(body.textBounds.max)).x+30,
            "same pooled preview root accepts later native text births and localization rebuilds");
        inventoryInk.enabled=comparisonInk.enabled=false;reference=Capture(camera,Path.Combine(output,"rebuilt-native-text-reference.png"));
        inventoryInk.enabled=comparisonInk.enabled=true;seated=Capture(camera,Path.Combine(output,"rebuilt-three-columns.png"));
        Check(BrightPixels(reference)>20&&BrightPixels(seated)>=BrightPixels(reference)-2,"later original text rebuild remains complete in actual pixels");
        Object.DestroyImmediate(reference);Object.DestroyImmediate(seated);

        // Build a native widget input from the audited307x65 original declaration.
        // Actual game ExtendedButton and real sprites/TMP render; gameplay owners/controllers
        // are supplied and never cloned. Full source/native asset hashes accompany this run.
        var rewardRoot=Rect("native reward source",root,1000,1000);rewardRoot.gameObject.SetActive(false);
        var rewards=rewardRoot.gameObject.AddComponent<UICampaignRewardWindow>();
        var nativeRect=Rect("Close Button",rewardRoot,307,65);var sourceImage=nativeRect.gameObject.AddComponent<Image>();sourceImage.color=Color.white;sourceImage.sprite=NativeSprite("normal");sourceImage.type=UnityEngine.UI.Image.Type.Sliced;
        var sourceGroup=nativeRect.gameObject.AddComponent<CanvasGroup>();
        var sourceButton=nativeRect.gameObject.AddComponent<native::ExtendedButton>();rewards.continueButton=sourceButton;
        sourceButton.navigation=new Navigation {mode=Navigation.Mode.None};
        sourceButton.targetGraphic=sourceImage;sourceButton.transition=Selectable.Transition.SpriteSwap;
        sourceButton.spriteState=new SpriteState {highlightedSprite=NativeSprite("m_HighlightedSprite"),pressedSprite=NativeSprite("m_PressedSprite"),selectedSprite=NativeSprite("m_SelectedSprite"),disabledSprite=NativeSprite("m_DisabledSprite")};
        sourceButton.colors=new ColorBlock {normalColor=Color.white,highlightedColor=new Color(.96f,.96f,.96f),pressedColor=new Color(.78f,.78f,.78f),selectedColor=Color.white,disabledColor=new Color(.78f,.78f,.78f,.5f),colorMultiplier=1,fadeDuration=.1f};
        sourceButton.buttonText=Text(nativeRect,"TextMeshPro Text","Continue",291,51);sourceButton.buttonText.fontSize=30;sourceButton.buttonText.enableAutoSizing=true;sourceButton.buttonText.fontSizeMin=18;sourceButton.buttonText.fontSizeMax=30;sourceButton.buttonText.color=new Color(.8980392f,.7843137f,.5333334f);sourceButton.buttonText.alignment=TextAlignmentOptions.Center;
        var keyHint=Rect("UI Controller Key Tracker Variant",nativeRect,32,32);keyHint.gameObject.SetActive(false);
        keyHint.gameObject.AddComponent<Image>();Text(keyHint,"Text (TMP)","",32,32);
        sourceButton.useAudioController=false;sourceButton.textLanguageKey="";
        Singleton<CampaignRewardsManager>.Instance=new CampaignRewardsManager {rewardsWindow=rewards};
        // A serialized source callback must not leak into the cloned view.
        var counter=rewardRoot.gameObject.AddComponent<LevelUpCallbackCounter>();
        UnityEditor.Events.UnityEventTools.AddPersistentListener(sourceButton.onClick,counter.Count);
        // First compare actual widget pixels without activating its original controller.
        rewardRoot.gameObject.SetActive(true);nativeRect.SetParent(level,false);
        nativeRect.anchoredPosition=new Vector2(0,-card.rect.height*.5f-65*.5f-20);
        Texture2D nativePixels=Capture(camera,Path.Combine(output,"original-native-continue.png"));
        nativeRect.SetParent(rewardRoot,false);rewardRoot.gameObject.SetActive(false);
        nativeRect.gameObject.AddComponent<native::ControllerInputClickeable>();
        owner.IsShowing=owner.enableTracker=true;AnnouncementContinue.Tick(true);
        var clone=level.GetComponentsInChildren<native::ExtendedButton>(true);
        native::ExtendedButton? view=null;foreach(var candidate in clone)if(candidate!=sourceButton)view=candidate;
        Check(view!=null&&((RectTransform)view.transform).rect.size==new Vector2(307,65)
            &&view.buttonText!=null&&ReferenceEquals(view.buttonText.font,sourceButton.buttonText.font),
            "native Continue retains complete original hierarchy geometry and font rather than reconstructed artwork");
        Check(view!.transform.childCount==nativeRect.childCount&&view.GetComponentsInChildren<Graphic>(true).Length==nativeRect.GetComponentsInChildren<Graphic>(true).Length
            &&view.GetComponent<CanvasGroup>().alpha==sourceGroup.alpha,
            "native Continue retains original graphic hierarchy, inactive controller hint and canvas-group presentation");
        Check(view!.onClick.GetPersistentEventCount()==0,"native Continue clone never retains serialized reward callbacks");
        Check(_controllerEnables==0,"native input controller clones never enable during inactive widget construction");
        Texture2D clonedPixels=Capture(camera,Path.Combine(output,"cloned-native-continue.png"));
        Check(Difference(nativePixels,clonedPixels)<.00001f,"native Continue widget preserves original rendered button and glyph pixels");
        int translations=_translations;for(int i=0;i<30;i++)AnnouncementContinue.Tick(true);
        Check(_translations==translations,"steady native Continue does not rewrite localization every frame");
        ExecuteEvents.Execute(view.gameObject,new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left},ExecuteEvents.pointerClickHandler);
        Check(_nativeButtonChecks>0&&owner.nextCardTracker.Clicks==1&&!owner.enableTracker,
            "native button gate false cannot veto the original ready tracker pointer permission");
        Check(counter.Calls==0,"view-only native Continue cannot execute source reward callbacks");
        Object.DestroyImmediate(nativePixels);Object.DestroyImmediate(clonedPixels);AnnouncementContinue.Tick(false);
        yield return null;

        // The original dialog is owned by DDOL; its level-up producer is scene-owned.
        var home=Rect("original persistent UI home",null,1920,1080);Object.DontDestroyOnLoad(home.gameObject);
        home.position=new Vector3(8,0,0);home.localScale=Vector3.one*.001f;
        var boxRect=Rect("original native confirmation",home,1920,1080);var box=boxRect.gameObject.AddComponent<ConfirmationBox>();var boxWindow=boxRect.gameObject.AddComponent<UIWindow>();
        Image(boxRect,"original confirmation body",620,230,Color.green);
        var manager=new UIConfirmationBoxManager {CurrentBox=box};Singleton<UIConfirmationBoxManager>.Instance=manager;
        Check(ModalFallback.WantsFloat(boxWindow),"unowned original keeps the existing generic float admission");
        Record(manager,owner);Check(CanvasConversion.Converts==0,"no separate dialog capture is created before semantic ownership admission");
        ConfirmationOnOwner.Tick();
        Check(ConfirmationOnOwner.IsOwned(boxWindow),"same native confirmation joins level-up before independent floating placement");
        Check(box.gameObject.scene.name=="DontDestroyOnLoad"&&!boxRect.IsChildOf(level),"persistent native dialog never inherits the scene-owned level-up lifetime");
        Check(CanvasConversion.Converts==1,"persistent member uses exactly one chrome-free capture");
        Check(!ModalFallback.WantsFloat(boxWindow),"owned confirmation cannot be readmitted as a generic floating window");
        Check(ModalFallback.DirectAdmission(boxWindow)&&ModalFallback.AdmissionAttempts==0,
            "direct generic conversion acknowledges existing embedded presentation without new capture or flat-screen failure");
        box.IsOpen=boxWindow.IsOpen=boxWindow.IsVisible=false;ConfirmationOnOwner.Tick();
        Check(!ConfirmationOnOwner.IsOwned(boxWindow)&&boxRect.parent==home,"native close restores exact persistent dialog home while owner remains open");
        box.IsOpen=boxWindow.IsOpen=boxWindow.IsVisible=true;Record(manager,owner);ConfirmationOnOwner.Tick();
        Check(ConfirmationOnOwner.IsOwned(boxWindow)&&CanvasConversion.Converts==2&&box.gameObject.scene.name=="DontDestroyOnLoad",
            "native pooled reopen reuses original dialog inside the same level-up with persistent lifetime");
        for(int i=0;i<3;i++)
        {
            root.SetPositionAndRotation(new Vector3(.08f*i,-.03f*i,.1f*i),Quaternion.Euler(0,i*7,0));root.localScale=Vector3.one*(.001f+i*.0002f);
            ConfirmationOnOwner.Tick();var member=CanvasConversion.ActivePanels.Find(p=>ReferenceEquals(p.Target,boxRect))!;
            Vector3 expected=level.TransformPoint(new Vector3(0,0,-32));
            Check(Vector3.Distance(member.HostRect.position,expected)<.00001f&&Vector3.Distance(member.HostRect.lossyScale,level.lossyScale)<.000001f,
                "persistent overlay copies only accepted owner world matrix through drag and resize");
            Texture2D pixels=Capture(camera,Path.Combine(output,"owned-dialog-"+i+".png"));Vector2 actual=GreenCenter(pixels);Vector3 wanted=camera.WorldToViewportPoint(expected);
            Check(Vector2.Distance(actual,new Vector2(wanted.x,wanted.y))*target.width<3,"original confirmation pixels stay in the physical owner across drag and resize");Object.DestroyImmediate(pixels);
        }
        int before=CanvasConversion.Converts;for(int i=0;i<20;i++)ConfirmationOnOwner.Tick();
        Check(CanvasConversion.Converts==before&&CanvasConversion.PoseAuthors==0,"persistent membership never creates repeated captures or an independent pose author");
        var unload=SceneManager.UnloadSceneAsync(scene);while(!unload.isDone)yield return null;
        ConfirmationOnOwner.Tick();
        Check(box!=null&&box.gameObject.scene.name=="DontDestroyOnLoad"&&box.IsOpen&&boxRect.parent==home,
            "owner scene unload restores the original persistent dialog without destroying or confirming it");
        Check(counter==null,"original scene-local source controller remains scene-local");
        Check(ModalFallback.WantsFloat(boxWindow),"owner release returns a still-open original dialog to its standalone admission");
        box!.IsOpen=boxWindow.IsOpen=boxWindow.IsVisible=false;ConfirmationOnOwner.Tick();
        Check(!ConfirmationOnOwner.IsOwned(boxWindow),"native hide retires exact embedded ownership without extra continuation");
        ConfirmationOnOwner.Reset();LevelUpComposite.Reset();harmony.UnpatchSelf();
        Object.DestroyImmediate(home.gameObject);Object.DestroyImmediate(camera.gameObject);target.Release();Object.DestroyImmediate(target);
        yield return _checks;
    }
}
public sealed class LevelUpCallbackCounter:MonoBehaviour {public int Calls;public void Count()=>Calls++;}
