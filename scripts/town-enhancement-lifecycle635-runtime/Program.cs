// The shipped UIEnchantressEffect, LoopAnimator, AnimationSetting, LeanTween and
// UILevelUpCardHolder run here, not replicas of their update/cancellation bodies.
// Only native shop/payment callbacks and the elapsed-time clock are boundaries.
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using GloomhavenVR.WorldUI;

public static class InteractionProgram
{
    private const BindingFlags Fields=BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance|BindingFlags.Static;
    private static int checks;
    private static void Check(bool condition,string reason){checks++;if(!condition)throw new Exception(reason);}
    private static void Set(object target,string name,object value)=>target.GetType().GetField(name,Fields)!.SetValue(target,value);
    private static T Get<T>(object target,string name)=>(T)target.GetType().GetField(name,Fields)!.GetValue(target)!;
    private static RectTransform Rect(string name,Transform? parent=null)
    {
        var rect=(RectTransform)new GameObject(name,typeof(RectTransform)).transform;
        rect.SetParent(parent,false);rect.sizeDelta=new Vector2(500,500);return rect;
    }
    public static int Run()
    {
        checks=0;LeanTween.reset();
        var flat=Rect("Inactive original flat shop");flat.gameObject.SetActive(false);
        var window=flat.gameObject.AddComponent<UIWindow>();
        var shop=flat.gameObject.AddComponent<UINewEnhancementWindow>();
        var original=Rect("CardHilight",flat);original.gameObject.SetActive(false);
        var aura=Rect("Aura",original);
        var types=Rect("Types",aura);var pulseGate=types.gameObject.AddComponent<CanvasGroup>();
        var buy=Rect("Buy",types);var sell=Rect("Sell",types);
        var loop=types.gameObject.AddComponent<LoopAnimator>();
        // Values from original level4 UIEnchantressEffect15436 and LoopAnimator11055.
        Set(loop,"effects",new List<AnimationSetting>{new AnimationSetting(types,TweenAction.CANVASGROUP_ALPHA,.6f,1f,2f,LeanTweenType.linear)});
        Set(loop,"autoStart",false);Set(loop,"ignoreTimeScale",false);
        var effect=aura.gameObject.AddComponent<UIEnchantressEffect>();
        Set(effect,"enchantressEffect",aura.gameObject);Set(effect,"rotationTime",20f);
        Set(effect,"rotationSpeed",1f);Set(effect,"idleAnimator",loop);
        Set(effect,"buyEffect",buy.gameObject);Set(effect,"sellEffect",sell.gameObject);
        Set(shop,"enhanctressEffect",effect);
        var converted=Rect("Converted original offered holder");
        original.SetParent(converted,false);original.gameObject.SetActive(true);
        TownServicePresentation.Window=window;TownServicePresentation.Service=3;
        typeof(TownServiceQuietController).GetField("_owner",Fields)!.SetValue(null,window);
        effect.ShowModeEffect(true);
        Check(buy.gameObject.activeSelf&&!sell.gameObject.activeSelf,"native effect retains original buy branch");
        TownServiceQuietController.SetOriginalEnhancementEffect(shop,false);
        Check(Get<LTDescr?>(effect,"rotationAnimation")==null&&!Get<bool>(loop,"looping"),"quiet prewarm has no hidden animation work");
        TownServiceQuietController.SetOriginalEnhancementEffect(shop,true);
        var rotation=Get<LTDescr?>(effect,"rotationAnimation");
        Check(rotation!=null&&LeanTween.isTweening(rotation.id),"original effect Play starts original LeanTween on the visible offered card");
        Check(Get<bool>(loop,"looping"),"original effect Play starts original LoopAnimator pulse");
        Check(!window.IsOpen&&!window.IsVisible&&window.Shows==0&&!flat.gameObject.activeSelf,"visible original effect does not open or activate the flat window");
        float initial=aura.eulerAngles.z;
        for(int frame=0;frame<90;frame++)
        {
            Pump(effect,loop);
            Check(Get<LTDescr?>(effect,"rotationAnimation")==rotation,"native clock advances without restarting original rotation");
        }
        float phase=aura.eulerAngles.z;
        Check(Mathf.Abs(Mathf.DeltaAngle(initial,phase))>15f&&Mathf.Abs(Mathf.DeltaAngle(initial,phase))<20f,"original LeanTween writes continuous native rotation over one second");
        Check(pulseGate.alpha<.85f&&pulseGate.alpha>.75f,"original LoopAnimator writes the serialized native alpha pulse");
        for(int frame=0;frame<25;frame++)TownServiceQuietController.SetOriginalEnhancementEffect(shop,true);
        Check(Get<LTDescr?>(effect,"rotationAnimation")==rotation,"routine mask refresh never restarts original effect");
        TownServiceQuietController.SetOriginalEnhancementEffect(shop,false);
        Check(Get<LTDescr?>(effect,"rotationAnimation")==null&&!LeanTween.isTweening(rotation!.id)&&!Get<bool>(loop,"looping"),"removed offering cancels original rotation and pulse");
        Check(Mathf.Abs(pulseGate.alpha-1f)<.0001f,"original Stop restores native pulse initial alpha");
        TownServiceQuietController.SetOriginalEnhancementEffect(shop,true);
        Check(Get<LTDescr?>(effect,"rotationAnimation")!=null&&Get<bool>(loop,"looping"),"next offering starts the same original effect again");
        TownServiceQuietController.Release();
        Check(Get<LTDescr?>(effect,"rotationAnimation")==null&&!Get<bool>(loop,"looping"),"quiet source release cancels original effect before hierarchy rollback");
        NativeAreaSelection();
        TeardownSelection(shop,window,flat);
        return checks;
    }
    public static void Pump(UIEnchantressEffect effect,LoopAnimator loop)
    {
        // Original engine code runs with a deterministic elapsed-time boundary.
        Get<LTDescr>(effect,"rotationAnimation").setUseManualTime(true);
        foreach(var tween in Get<List<LTDescr>>(loop,"currentAnim"))tween.setUseManualTime(true);
        LeanTween.dtManual=1f/90f;
        typeof(LeanTween).GetField("frameRendered",Fields)!.SetValue(null,-1);
        LeanTween.update();
    }
    private static void NativeAreaSelection()
    {
        // Native platform initialization and navigation/payment services are outside
        // this click proof. The original VR mouse-mode guard's observable flag is
        // supplied, while the actual area Awake/ExtendedButton event/OnClick run.
        var platformGo = new GameObject("Inactive native PC platform boundary");
        platformGo.SetActive(false);
        PlatformLayer? previousPlatform = PlatformLayer.Instance;
        PlatformLayer.Instance = platformGo.AddComponent<PlatformLayer>();
        typeof(InputManager).GetField("isUseGamepadInPc",Fields)!.SetValue(null,false);
        Check(!InputManager.GamePadInUse,"original PC input flag matches the installed VR mouse-mode guard");
        var rect=Rect("Original native enhancement area input");rect.gameObject.SetActive(false);
        var button=rect.gameObject.AddComponent<ExtendedButton>();
        button.onMouseEnter=new UnityEvent();button.onMouseExit=new UnityEvent();
        button.onSelected=new UnityEvent();button.onDeselected=new UnityEvent();button.useAudioController=false;
        var frame=Rect("Original native area frame",rect).gameObject.AddComponent<Image>();
        var fill=Rect("Original native area fill",rect);fill.gameObject.AddComponent<Image>();
        var fillGate=fill.gameObject.AddComponent<CanvasGroup>();
        var nativeArea=rect.gameObject.AddComponent<UIEnhancementButtonHighlight>();
        Set(nativeArea,"frame",frame);Set(nativeArea,"fillImage",fillGate);
        var ability=Rect("Original native enhancement ability").gameObject.AddComponent<EnhancementButtonBase>();
        Set(nativeArea,"ability",ability);
        int selected=0;
        Set(nativeArea,"onSelectedAbility",(Action<EnhancementButtonBase>)(chosen=>{Check(ReferenceEquals(chosen,ability),"native area callback receives its original enhancement ability");selected++;}));
        rect.gameObject.SetActive(true);
        button.interactable=true;
        Check((bool)nativeArea.GetType().GetMethod("IsInteractable",Fields)!.Invoke(nativeArea,null)!,"native area mouse click does not depend on absent gamepad navigation initialization");
        // Awake wires this exact original ExtendedButton event to original OnClick.
        // The native callback occurs before the native navigation state transition;
        // that transition has no booted singleton in this bounded fixture.
        bool missingNavigation=false;
        try { button.onClick.Invoke(); }
        catch (NullReferenceException) { missingNavigation=true; }
        Check(selected==1,"actual original ExtendedButton OnClick invokes native area selection immediately");
        Check(missingNavigation,"native navigation startup is an explicit fixture boundary after selection");
        // The native OnDisable consults the same PC platform boundary. Retire this
        // input island before restoring it, so fixture cleanup cannot invent errors.
        UnityEngine.Object.DestroyImmediate(rect.gameObject);
        PlatformLayer.Instance=previousPlatform;
    }
    private static void TeardownSelection(UINewEnhancementWindow shop,UIWindow window,RectTransform parent)
    {
        var outer=Rect("Actual original UIEnhancementCardHighlighter",parent);outer.gameObject.SetActive(false);
        var holderRect=Rect("Original UILevelUpCardHolder",outer);
        var holder=holderRect.gameObject.AddComponent<UILevelUpCardHolder>();
        var gate=holderRect.gameObject.AddComponent<CanvasGroup>();Set(holder,"canvasGroup",gate);
        var highlighter=outer.gameObject.AddComponent<UIEnhancementCardHighlighter>();
        Set(highlighter,"cardHolder",holder);Set(highlighter,"canvasGroup",outer.gameObject.AddComponent<CanvasGroup>());
        Set(highlighter,"cardHighlightEffect",Rect("Original animated card frame",outer).gameObject);
        shop.cardHolder=highlighter;
        TownServicePresentation.Window=window;TownServicePresentation.Service=3;
        var route=new NativeHandoffFixture(shop,window);
        int beforeDeselections=shop.Deselections,beforeSelections=shop.Selections;
        route.Clear();
        Check(shop.Deselections==beforeDeselections+1&&shop.Selections==beforeSelections+1,"live original selection remains routed through native highlighter Hide");
        shop.ThrowSelect=true;bool liveFailure=false;
        try{route.Clear();}catch(Exception error){liveFailure=error.Message=="live native selection failure";}
        Check(liveFailure,"genuine live native callback failure remains visible");shop.ThrowSelect=false;
        UnityEngine.Object.DestroyImmediate(gate);
        bool nativeFailure=false;
        try{highlighter.Hide();}catch(Exception error){nativeFailure=error is NullReferenceException||error is MissingReferenceException;}
        Check(nativeFailure,"shipped original highlighter reproduces destroyed CanvasGroup teardown exception");
        int selections=shop.Selections,deselections=shop.Deselections;
        try { route.Clear(); }
        catch (Exception error) { throw new Exception("destroyed native CanvasGroup callback escaped", error); }
        Check(shop.Selections==selections&&shop.Deselections==deselections,"destroyed serialized native CanvasGroup prevents only the teardown callback");
    }
}
