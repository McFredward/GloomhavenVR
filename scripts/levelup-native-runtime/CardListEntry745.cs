extern alias native;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Xml;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static partial class LevelUpNativeProgram
{
    private sealed class ListCharacter745 : RealProxy
    {
        internal ListCharacter745(Type type):base(type) { }
        public override IMessage Invoke(IMessage message)
        {
            var call=(IMethodCallMessage)message;var method=(MethodInfo)call.MethodBase;
            object? result=method.Name=="get_Level"?1:method.ReturnType==typeof(void)?null:
                method.ReturnType.IsValueType?Activator.CreateInstance(method.ReturnType):null;
            return new ReturnMessage(result,null,0,call.LogicalCallContext,call);
        }
    }
    private static UIWindow? _listFixtureWindow745;
    private static int _listOpened745,_listClosed745;
    private static bool _allowList745;
    private static native::UnityEngine.UI.UIWindow? _nativeListWindow748;
    private static object? _nativeListFade748;
    private static bool NativeListShow745(bool locked,bool isOpenByToggle)
    {
        Check(locked&&isOpenByToggle,"original native browse callback retains locked cards and toggle provenance");
        _listOpened745++;
        if(_nativeListWindow748!=null)_nativeListWindow748.Show(true);
        _listFixtureWindow745!.IsOpen=_nativeListWindow748?.IsOpen??true;return false;
    }
    private static bool NativeListHide745()
    {
        _listClosed745++;
        if(_nativeListWindow748!=null)return true;
        _listFixtureWindow745!.IsOpen=false;return false;
    }
    private static void NativeListHidden748()
    {
        if(_nativeListWindow748==null||_listFixtureWindow745==null)return;
        _listFixtureWindow745.IsOpen=_nativeListWindow748.IsOpen;
        _listFixtureWindow745.IsVisible=_nativeListWindow748.IsVisible;
    }
    private static bool NativeListFade748(object info)
    {_nativeListFade748=info;return false;}
    private static void PulseListFade748(float fraction)
    {
        object fade=_nativeListFade748!;
        AccessTools.Method(fade.GetType(),"TweenValue").Invoke(fade,new object[]{fraction});
        if(fraction==1f)AccessTools.Method(fade.GetType(),"Finished").Invoke(fade,null);
        NativeListHidden748();
    }
    private static bool NativeListPeripheral745()=>false;
    private static bool NativeListGamepad745(ref bool __result){__result=false;return false;}
    private static bool NativeListGate745(ref bool __result){__result=_allowList745;return false;}

    public static IEnumerator RunCardListEntry745(string output)
    {
        _checks=0;WorldUIConfig.ConversionActive=true;FlatScreen.ManualScreenActive=false;
        GloomhavenVR.Core.Events.VRModeStateMachine.TableInFrontOfPlayer=true;VRSession.IsRunning=true;Loc.SetLanguage("English");NativeFont();
        var harmony=_harmony=new Harmony("GloomhavenVR.CardList745."+typeof(LevelUpNativeProgram).Assembly.GetName().Name);
        var proof=CardListEntry745(output,harmony);while(proof.MoveNext())yield return proof.Current;
        harmony.UnpatchSelf();yield return _checks;
    }

    // Actual native Show/ToggleDisplay and ExtendedToggle.OnPointerClick execute.
    // Downstream inventory population, controller focus and escapable registration
    // are declared ports: this fixture never instantiates a gameplay campaign.
    private static IEnumerator CardListEntry745(string output,Harmony harmony)
    {
        File.WriteAllText(Path.Combine(output,"native-card-list-limits745.txt"),
            "Exact production CardListEntry/LevelUpComposite, original static Cards header and browse-toggle artwork/fonts, actual game ExtendedToggle.OnPointerClick and native UIResetLevelUpWindow.Show/ToggleDisplay execute. The caption is raycast and pressed outside the original icon bounds. Native ICharacterService is a declared proxy (Level1); downstream campaign inventory population, controller focus, escapable registration and session input policy are explicit ports. Unrelated native GUI_FlexFrame level-up glow/warning shader branches are omitted from the GL header picture, with full authored export retained. This is not native campaign bootstrap, HMD pacing, multiplayer or Windows shader acceptance.");
        var game=typeof(native::ExtendedToggle).Assembly;
        var nativeResetType=typeof(native::UIResetLevelUpWindow);
        var nativeInventoryType=typeof(native::UILevelUpCardInventory);
        harmony.Patch(AccessTools.Method(nativeInventoryType,"Show"),prefix:new HarmonyMethod(Method(nameof(NativeListShow745))));
        harmony.Patch(AccessTools.Method(nativeInventoryType,"Hide"),prefix:new HarmonyMethod(Method(nameof(NativeListHide745))));
        harmony.Patch(AccessTools.Method(nativeInventoryType,"EnableInteraction"),prefix:new HarmonyMethod(Method(nameof(NativeListPeripheral745))));
        foreach(string method in new[]{"Focus","Unfocus"})
            foreach(var focusMethod in typeof(native::ControllerInputArea).GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Where(m=>m.Name==method))
                harmony.Patch(focusMethod,prefix:new HarmonyMethod(Method(nameof(NativeListPeripheral745))));
        foreach(string method in new[]{"RegisterEscapable","UnregisterEscapable"})
            harmony.Patch(AccessTools.Method(typeof(native::UnityEngine.UI.UIWindowManager),method),prefix:new HarmonyMethod(Method(nameof(NativeListPeripheral745))));
        harmony.Patch(AccessTools.PropertyGetter(game.GetType("InputManager"),"GamePadInUse"),prefix:new HarmonyMethod(Method(nameof(NativeListGamepad745))));
        harmony.Patch(AccessTools.Method(game.GetType("InteractabilityManager"),"ShouldAllowClickForExtendedToggle"),prefix:new HarmonyMethod(Method(nameof(NativeListGate745))));

        var root=Rect("native closed-list character",null,1920,1080);root.localScale=Vector3.one*.001f;
        var canvas=root.gameObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;
        var head=new GameObject("closed-list physical eye").AddComponent<Camera>();head.enabled=false;
        head.orthographic=true;head.orthographicSize=.8f;head.transform.position=new Vector3(0,0,-3);
        head.clearFlags=CameraClearFlags.SolidColor;head.backgroundColor=Color.black;head.cullingMask=1<<5;
        head.targetTexture=new RenderTexture(1000,720,24);head.targetTexture.Create();canvas.worldCamera=head;
        var events=new GameObject("closed-list native events",typeof(EventSystem));
        var party=root.gameObject.AddComponent<NewPartyDisplayUI>();party.window=root.gameObject.AddComponent<UIWindow>();
        party.window.ID=UIWindowID.PartyPanel;party.ActiveDisplay=NewPartyDisplayUI.DisplayType.CARDS;NewPartyDisplayUI.PartyDisplay=party;
        var cardTree=new XmlDocument();cardTree.Load(Path.Combine(_inputs,"native-card-list-view.xml"));
        // Native level-up pulsing warning/highlight shader is outside this
        // closed-list entry proof. Keep its untouched authored export; omit only
        // these unrelated effects from the static GL header presentation.
        foreach(XmlNode effect in cardTree.SelectNodes(".//component[@shader='GUI/GUI_FlexFrame_Shd']/.."))effect.ParentNode!.RemoveChild(effect);
        var view=ImportNode739(cardTree.DocumentElement!,root,new Dictionary<string,TMP_FontAsset>());view.gameObject.SetActive(true);
        party.AbilityCardsDisplay=view.gameObject.AddComponent<NativeCardsDisplay>();
        var title=view.Find("Container/Header/Title").GetComponent<TextMeshProUGUI>();
        title.text="Ability Cards";
        Check(view.rect.width==388&&title.gameObject.activeInHierarchy,"original native Cards view keeps its authored frame and active header");
        head.transform.position=new Vector3(view.TransformPoint(view.rect.center).x,0,-3);
        var inventory=Rect("native closed inventory sibling",null,368,1080);
        var cards=inventory.gameObject.AddComponent<UILevelUpCardInventory>();_listFixtureWindow745=inventory.gameObject.AddComponent<UIWindow>();_listFixtureWindow745.IsOpen=false;
        var entryTree=new XmlDocument();entryTree.Load(Path.Combine(_inputs,"native-card-list-entry.xml"));
        var arrow=ImportNode739(entryTree.DocumentElement!,inventory,new Dictionary<string,TMP_FontAsset>());
        Check(arrow.rect.size==new Vector2(43,65),"original native card-list arrow keeps authored43x65 geometry");
        var image=arrow.GetComponent<Image>();
        var toggle=arrow.gameObject.AddComponent<native::ExtendedToggle>();toggle.targetGraphic=image;
        toggle.graphic=arrow.Find("On").GetComponent<Image>();
        toggle.onMouseEnter=new UnityEvent();toggle.onMouseExit=new UnityEvent();toggle.onSelected=new UnityEvent();toggle.onDeselected=new UnityEvent();
        toggle.scaleWhenSelected=false;toggle.animateScaling=false;toggle.transition=Selectable.Transition.None;
        var originalGroup=arrow.gameObject.AddComponent<CanvasGroup>();originalGroup.ignoreParentGroups=true;
        arrow.gameObject.SetActive(false);
        var reset=inventory.gameObject.AddComponent<UIResetLevelUpWindow>();reset.inventory=cards;reset.displayToggle=toggle;
        Singleton<UIResetLevelUpWindow>.Instance=reset;Singleton<UILevelUpWindow>.Instance=null;
        var panel=new ConvertedPanel {Target=root,HostRect=root,HostGo=root.gameObject,HostCanvas=canvas,HostRaycaster=root.gameObject.AddComponent<GraphicRaycaster>()};
        CanvasConversion.ActivePanels.Add(panel);

        var nativeHolder=new GameObject("inactive native list services");nativeHolder.SetActive(false);
        var nativeInventory=nativeHolder.AddComponent<native::UILevelUpCardInventory>();
        var nativeReset=nativeHolder.AddComponent<native::UIResetLevelUpWindow>();
        var controller=nativeHolder.AddComponent<native::ControllerInputArea>();
        var dummyReset=nativeHolder.AddComponent<native::ExtendedButton>();
        AccessTools.Field(nativeResetType,"displayToggle").SetValue(nativeReset,toggle);
        AccessTools.Field(nativeResetType,"inventory").SetValue(nativeReset,nativeInventory);
        AccessTools.Field(nativeResetType,"controllerArea").SetValue(nativeReset,controller);
        AccessTools.Field(nativeResetType,"resetButton").SetValue(nativeReset,dummyReset);
        var resetControls=new GameObject("native reset controls");resetControls.transform.SetParent(nativeHolder.transform,false);
        AccessTools.Field(nativeResetType,"resetPanel").SetValue(nativeReset,resetControls);
        AccessTools.Field(nativeResetType,"resetGoldInfo").SetValue(nativeReset,resetControls);
        AccessTools.Field(nativeResetType,"resetCostText").SetValue(nativeReset,Text((RectTransform)resetControls.AddComponent<RectTransform>(),"native reset cost","0",100,30));
        var notifications=AccessTools.Field(nativeResetType,"newCardNotifications");notifications.SetValue(nativeReset,Activator.CreateInstance(notifications.FieldType));
        var originalToggle=AccessTools.Method(nativeResetType,"ToggleDisplay");
        toggle.onValueChanged.AddListener((UnityAction<bool>)Delegate.CreateDelegate(typeof(UnityAction<bool>),nativeReset,originalToggle));
        var show=AccessTools.Method(nativeResetType,"Show");var service=new ListCharacter745(show.GetParameters()[0].ParameterType).GetTransparentProxy();
        _listOpened745=_listClosed745=0;_allowList745=true;
        var enableShow=AccessTools.Method(nativeResetType,"EnableShow");
        enableShow.Invoke(nativeReset,new object[]{false,toggle});reset.requestDisableShow.Add(toggle);
        show.Invoke(nativeReset,new[]{service});LevelUpComposite.Tick();
        Check(!arrow.gameObject.activeSelf&&ReferenceEquals(arrow.parent,inventory)&&_listOpened745==0&&_listClosed745==0,
            "original native show lock prevents opener activation before mod presentation admission");
        enableShow.Invoke(nativeReset,new object[]{true,toggle});reset.requestDisableShow.Clear();
        show.Invoke(nativeReset,new[]{service}); // Exact original activates the closed-list arrow.
        Check(arrow.gameObject.activeSelf&&!_listFixtureWindow745.IsOpen,"original native Show activates opener while candidate inventory remains closed");
        int initialClose=_listClosed745;LevelUpComposite.Tick();
        Check(arrow.IsChildOf(view)&&!arrow.IsChildOf(inventory),"native closed-list opener is reachable inside converted character Cards tab before list opens");
        var caption=arrow.GetComponentsInChildren<TextMeshProUGUI>(true).Single(t=>t.name=="GloomhavenVR.MoreCardsLabel");
        Check(caption.text=="More Cards"&&ReferenceEquals(caption.font,title.font),"visible More Cards caption uses original card typography");
        Canvas.ForceUpdateCanvases();yield return null;caption.ForceMeshUpdate();
        Check(!caption.isTextTruncated&&caption.textInfo.characterCount==caption.text.Length&&caption.textInfo.characterInfo.Where(c=>!char.IsWhiteSpace(c.character)).All(c=>c.isVisible),"complete English card-list caption renders in the native header band");
        var corners=new Vector3[4];caption.rectTransform.GetWorldCorners(corners);
        Check(corners.All(c=>view.rect.Contains((Vector2)view.InverseTransformPoint(c))),"card-list entry caption stays inside original native Cards frame");
        var bottom=view.InverseTransformPoint(corners[0]);var top=view.InverseTransformPoint(corners[1]);
        Check(view.rect.yMax-top.y>=32&&view.rect.yMax-bottom.y<74.546f,"card-list caption occupies the free band between original game header rows");
        Object.Destroy(Capture(head,Path.Combine(output,"closed-list-entry-visible.png")));
        var pointer=new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left,position=RectTransformUtility.WorldToScreenPoint(head,caption.transform.TransformPoint(caption.rectTransform.rect.center))};
        var hits=new List<RaycastResult>();panel.HostRaycaster.Raycast(pointer,hits);
        var hit=hits.FirstOrDefault(h=>h.gameObject==caption.gameObject);Check(hit.gameObject!=null,"native graphic raycast hits More Cards caption");
        Vector3 localClick=arrow.InverseTransformPoint(caption.transform.TransformPoint(caption.rectTransform.rect.center));
        Check(!arrow.rect.Contains((Vector2)localClick),"caption click is outside original43px arrow icon bounds");
        Quaternion arrowRotation=arrow.localRotation,captionRotation=caption.rectTransform.localRotation;
        var target=ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit.gameObject);
        Check(ReferenceEquals(target,toggle.gameObject),"caption resolves original ExtendedToggle pointer handler");
        ExecuteEvents.Execute(target,pointer,ExecuteEvents.pointerClickHandler);LevelUpComposite.Tick();
        Check(_listOpened745==1&&toggle.isOn&&inventory.IsChildOf(view),"caption click executes original native toggle callback and opens list inside same character window");
        Check(arrow.localRotation==arrowRotation&&caption.rectTransform.localRotation==captionRotation,"native arrow on state retains upright label orientation");
        _allowList745=false;ExecuteEvents.Execute(target,pointer,ExecuteEvents.pointerClickHandler);
        Check(toggle.isOn&&_listOpened745==1&&_listClosed745==initialClose,"native toggle interactability policy is not bypassed by caption");
        _allowList745=true;toggle.interactable=false;ExecuteEvents.Execute(target,pointer,ExecuteEvents.pointerClickHandler);
        Check(toggle.isOn&&_listClosed745==initialClose,"native disabled toggle retains its original restriction through caption");toggle.interactable=true;
        Loc.SetLanguage("German");Check(caption.text=="Weitere Karten","native card-list caption follows German language switch");
        Canvas.ForceUpdateCanvases();yield return null;caption.ForceMeshUpdate();
        Check(!caption.isTextTruncated&&caption.textInfo.characterCount==caption.text.Length&&caption.textInfo.characterInfo.Where(c=>!char.IsWhiteSpace(c.character)).All(c=>c.isVisible),"complete German card-list caption renders in original font within original Cards header");
        Object.Destroy(Capture(head,Path.Combine(output,"closed-list-entry-german.png")));
        ExecuteEvents.Execute(target,pointer,ExecuteEvents.pointerClickHandler);LevelUpComposite.Tick();
        Check(!toggle.isOn&&!_listFixtureWindow745.IsOpen&&_listClosed745==initialClose+1,"same original toggle callback closes native list without a level-up");
        Check(ReferenceEquals(inventory.parent,null)&&arrow.IsChildOf(view),"closed native list returns home while original opener remains available");
        enableShow.Invoke(nativeReset,new object[]{false,toggle});reset.requestDisableShow.Add(toggle);LevelUpComposite.Tick();
        Check(ReferenceEquals(arrow.parent,inventory),"live native level-up show lock returns the original browse opener without changing its value");
        enableShow.Invoke(nativeReset,new object[]{true,toggle});reset.requestDisableShow.Clear();LevelUpComposite.Tick();
        Check(arrow.IsChildOf(view)&&!toggle.isOn,"original browse opener resumes after native level-up show lock ends");
        var retirement=BrowseRetirement748(output,harmony,party,reset,inventory,view,panel,nativeReset,nativeInventory,show,service,toggle);
        while(retirement.MoveNext())yield return retirement.Current;
        Loc.SetLanguage("English");LevelUpComposite.Reset();
        Check(ReferenceEquals(arrow.parent,inventory)&&arrow.rect.size==new Vector2(43,65),"character-window teardown restores original toggle home and authored frame");
        Object.DestroyImmediate(nativeHolder);Object.DestroyImmediate(inventory.gameObject);Object.DestroyImmediate(root.gameObject);
        Object.DestroyImmediate(head.targetTexture);Object.DestroyImmediate(head.gameObject);Object.DestroyImmediate(events);
        CanvasConversion.ActivePanels.Remove(panel);Singleton<UIResetLevelUpWindow>.Instance=null;NewPartyDisplayUI.PartyDisplay=null;_listFixtureWindow745=null;
    }

    private static IEnumerator BrowseRetirement748(string output,Harmony harmony,NewPartyDisplayUI party,
        UIResetLevelUpWindow reset,RectTransform inventory,RectTransform view,ConvertedPanel panel,
        native::UIResetLevelUpWindow nativeReset,native::UILevelUpCardInventory nativeInventory,
        MethodInfo show,object service,Toggle toggle)
    {
        File.WriteAllText(Path.Combine(output,"browse-retirement748-boundary.txt"),
            "Production LevelUpComposite, direct/ordinary admission and ReleaseForComposite execute. Original UIResetLevelUpWindow.Hide, ToggleDisplay(false), UILevelUpCardInventory.Hide and native UIWindow.Show/Hide/EvaluateAndTransitionToVisualState/StartAlphaTween execute. Native gameplay population, controller/escape services and global clock are explicit ports. The original FloatTween value/finished callbacks are clocked manually; boundary owner/window fields mirror actual native IsOpen/IsVisible after original calls. No actor ResetAbilities/OnResetLevel/choice/confirmation is invoked.");
        var nativeWindow=inventory.gameObject.AddComponent<native::UnityEngine.UI.UIWindow>();
        _nativeListWindow748=nativeWindow;
        var nativeType=typeof(native::UnityEngine.UI.UIWindow);
        AccessTools.Field(nativeType,"m_FocusedAction").SetValue(nativeWindow,native::UnityEngine.UI.UIWindow.FocusedAction.None);
        AccessTools.Field(nativeType,"m_Transition").SetValue(nativeWindow,native::UnityEngine.UI.UIWindow.Transition.Fade);
        AccessTools.Field(nativeType,"m_TransitionDuration").SetValue(nativeWindow,.1f);
        AccessTools.Field(typeof(native::UILevelUpCardInventory),"window").SetValue(nativeInventory,nativeWindow);
        AccessTools.Field(typeof(native::UILevelUpCardInventory),"windowRectTransform").SetValue(nativeInventory,inventory);
        var runner=AccessTools.Field(nativeType,"m_FloatTweenRunner").GetValue(nativeWindow);
        harmony.Patch(AccessTools.Method(runner.GetType(),"StartTween"),prefix:new HarmonyMethod(Method(nameof(NativeListFade748))));
        harmony.Patch(AccessTools.Method(typeof(native::UILevelUpCardInventory),"Hide"),postfix:new HarmonyMethod(Method(nameof(NativeListHidden748))));
        reset.OnNativeHide=nativeReset.Hide;
        try
        {
            toggle.isOn=true;show.Invoke(nativeReset,new[]{service});LevelUpComposite.Tick();
            Check(nativeWindow.IsOpen&&inventory.IsChildOf(view)&&LevelUpComposite.IsOwnedInventory(_listFixtureWindow745!),
                "native browse opening has one proved seated inventory before commitment");
            int closes=_listClosed745;
            QuestJourneyCurtain.PartyCommitted=true;
            LevelUpComposite.Tick();
            Check(!nativeWindow.IsOpen&&_listClosed745==closes+1&&toggle.isOn,
                "commitment closes proved browsing through original Hide while preserving the native toggle preference");
            Check(nativeWindow.IsVisible&&_listFixtureWindow745!.IsVisible&&inventory.parent==null,
                "original fading browse returns to its true native home while still drawable");
            int attempts=ModalFallback.AdmissionAttempts;
            Check(!ModalFallback.WantsFloat(_listFixtureWindow745!)&&ModalFallback.DirectAdmission(_listFixtureWindow745!)
                &&ModalFallback.AdmissionAttempts==attempts,
                "retired native-home browser cannot refloat through ordinary or direct admission during its fade");
            var unrelated=Rect("unrelated native inventory748",null,368,1080);
            unrelated.gameObject.AddComponent<UILevelUpCardInventory>();var unrelatedWindow=unrelated.gameObject.AddComponent<UIWindow>();
            Check(ModalFallback.WantsFloat(unrelatedWindow)&&!ModalFallback.DirectAdmission(unrelatedWindow),
                "unrelated native inventory keeps its independent admission permission");
            Object.DestroyImmediate(unrelated.gameObject);
            Check(_nativeListFade748!=null&&(float)AccessTools.Property(_nativeListFade748.GetType(),"duration").GetValue(_nativeListFade748)==.1f,
                "browse dismissal retains the original native fade duration and callbacks");
            PulseListFade748(.5f);Check(nativeWindow.IsVisible&&!nativeWindow.IsOpen,"original half-faded browse remains closed and drawable");
            PulseListFade748(1f);Check(!nativeWindow.IsVisible&&!nativeWindow.IsOpen,"original browse fade completes without an independent window");
            show.Invoke(nativeReset,new[]{service});
            Check(nativeWindow.IsOpen,"native later Show attempt retains its original callback before retirement guard");
            Check(!ModalFallback.WantsFloat(_listFixtureWindow745!)&&ModalFallback.DirectAdmission(_listFixtureWindow745!),
                "commitment retirement rejects later native Show admission before the next owner tick");
            LevelUpComposite.Tick();Check(!nativeWindow.IsOpen&&toggle.isOn,
                "later native Show during commitment closes through the same original browse flow");
            QuestJourneyCurtain.PartyCommitted=false;show.Invoke(nativeReset,new[]{service});LevelUpComposite.Tick();
            Check(nativeWindow.IsOpen&&inventory.IsChildOf(view),"fresh native Cards owner recovers browsing after commitment ends");
            party.window.IsOpen=false;LevelUpComposite.Tick();
            Check(!nativeWindow.IsOpen&&inventory.parent==null&&!ModalFallback.WantsFloat(_listFixtureWindow745!),
                "loss of the exact character owner retires only its proved browsing inventory");
            var levelRoot=Rect("fresh mandatory level-up owner748",null,1600,1080);
            var level=levelRoot.gameObject.AddComponent<UILevelUpWindow>();level.myWindow=levelRoot.gameObject.AddComponent<UIWindow>();
            level.inventory=reset.inventory;Singleton<UILevelUpWindow>.Instance=level;
            QuestJourneyCurtain.PartyCommitted=true;show.Invoke(nativeReset,new[]{service});
            int beforeMandatory=_listClosed745;LevelUpComposite.Tick();
            Check(nativeWindow.IsOpen&&_listClosed745==beforeMandatory,
                "fresh mandatory native level-up overrides the old browse receipt during commitment");
            Check(ModalFallback.WantsFloat(_listFixtureWindow745!),
                "unseated mandatory native level-up keeps its original independent fallback permission");
            var levelPanel=new ConvertedPanel{Target=levelRoot,HostRect=levelRoot,HostGo=levelRoot.gameObject};
            CanvasConversion.ActivePanels.Add(levelPanel);LevelUpComposite.Tick();
            Check(nativeWindow.IsOpen&&_listClosed745==beforeMandatory&&inventory.IsChildOf(levelRoot)
                &&!ModalFallback.WantsFloat(_listFixtureWindow745!)&&ModalFallback.DirectAdmission(_listFixtureWindow745!),
                "seated mandatory native level-up retains one owner presentation without browse dismissal");
            LevelUpComposite.Reset();CanvasConversion.ActivePanels.Remove(levelPanel);
            Singleton<UILevelUpWindow>.Instance=null;Object.DestroyImmediate(levelRoot.gameObject);
            yield return null;
        }
        finally
        {
            reset.OnNativeHide=null;_nativeListWindow748=null;_nativeListFade748=null;
            QuestJourneyCurtain.PartyCommitted=false;StoryComposite.StoryOrLoadoutStanding=false;party.window.IsOpen=true;
            Singleton<UILevelUpWindow>.Instance=null;
        }
    }
}
