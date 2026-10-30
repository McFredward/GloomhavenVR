using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Code.State;
using ClockStone;
using HarmonyLib;
using Script.GUI.SMNavigation;
using Script.GUI.SMNavigation.States.CampaignMapStates;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.WorldUI.MenuDepth;
using Object=UnityEngine.Object;

public static partial class MenuDepthLaserProgram
{
    // Real native pointer/preview bodies execute. Campaign bootstrap, artwork,
    // screen-fit delta and decoration are independent fixture inputs. In this
    // focused input proof the native highlight animation uses its immediate path.
    private static Harmony NativePorts()
    {
        var harmony=new Harmony("MenuDepthLaser.Native."+typeof(MenuDepthLaserProgram).Assembly.GetName().Name);
        harmony.CreateClassProcessor(typeof(NativeLifecycle)).Patch();
        harmony.CreateClassProcessor(typeof(NativeGamepad)).Patch();
        harmony.CreateClassProcessor(typeof(NativeFit)).Patch();
        harmony.CreateClassProcessor(typeof(NativeAudioLookup)).Patch();
        harmony.CreateClassProcessor(typeof(NativeAudioOutput)).Patch();
        return harmony;
    }

    [HarmonyPatch]
    private static class NativeLifecycle
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach(Type type in new[]{typeof(FullAbilityCard),typeof(MiniAbilityCard),typeof(AbilityCardUI),typeof(ItemCardUI),typeof(UILevelUpCard)})
                foreach(string name in new[]{"Awake","Start","OnEnable","OnDisable","OnDestroy","Update","LateUpdate"})
                {
                    MethodInfo method=AccessTools.DeclaredMethod(type,name);
                    if(method!=null)yield return method;
                }
            yield return AccessTools.Method(typeof(UILevelUpCard),"SetBackground");
            yield return AccessTools.Method(typeof(FullAbilityCard),"Highlight");
            yield return AccessTools.Method(typeof(FullAbilityCard),"ToggleBurningAnimation");
            yield return AccessTools.Method(typeof(ExtendedButton),"Update");
            foreach(MethodInfo method in AccessTools.GetDeclaredMethods(typeof(AbilityCardUI)))
                if(method.Name=="SetDisplayMode")yield return method;
        }
        [HarmonyPrefix] private static bool Before()=>false;
    }

    [HarmonyPatch(typeof(InputManager),"get_GamePadInUse")]
    private static class NativeGamepad
    { [HarmonyPrefix] private static bool Before(ref bool __result){__result=false;return false;} }

    [HarmonyPatch(typeof(RectTransformExtensions),nameof(RectTransformExtensions.DeltaWorldPositionToFitTheScreen),
        new[]{typeof(RectTransform),typeof(Camera),typeof(float),typeof(float)})]
    private static class NativeFit
    { [HarmonyPrefix] private static bool Before(ref Vector3 __result){__result=Vector3.zero;return false;} }

    // Audio-bank availability and device output are boundaries. The actual
    // ExtendedButton.OnHighlight/PlaySound bodies choose the original profile.
    private static readonly List<string> AudioOutput=new();
    [HarmonyPatch(typeof(AudioController),nameof(AudioController.GetAudioItem))]
    private static class NativeAudioLookup
    { [HarmonyPrefix] private static bool Before(ref AudioItem __result){__result=new AudioItem();return false;} }
    [HarmonyPatch(typeof(AudioControllerUtils),nameof(AudioControllerUtils.PlaySound))]
    private static class NativeAudioOutput
    { [HarmonyPrefix] private static bool Before(string audioItem){AudioOutput.Add(audioItem);return false;} }

    [DataContract] private sealed class AuthoredAudio
    {
        [DataMember] public string mouseClickAudioItem="",mouseDownAudioItem="",mouseUpAudioItem="",
            mouseEnterAudioItem="",mouseExitAudioItem="",nonInteractableMouseDownAudioItem="";
        internal AudioButtonProfile Profile()
        {
            var profile=ScriptableObject.CreateInstance<AudioButtonProfile>();
            profile.mouseClickAudioItem=mouseClickAudioItem;profile.mouseDownAudioItem=mouseDownAudioItem;
            profile.mouseUpAudioItem=mouseUpAudioItem;profile.mouseEnterAudioItem=mouseEnterAudioItem;
            profile.mouseExitAudioItem=mouseExitAudioItem;profile.nonInteractableMouseDownAudioItem=nonInteractableMouseDownAudioItem;
            return profile;
        }
    }
    [DataContract] private sealed class AuthoredRow
    {
        [DataMember] public float width,height,rowAlpha,highlightScale,animationDuration;
        [DataMember] public Vector2 pivot;
        [DataMember] public Vector3 hoverMovement;
        [DataMember] public bool rowRaycast,holderBlocksRaycasts,holderInteractable,scaleNonInteractable,useAudioController;
        [DataMember] public AuthoredAudio audio=null!,audioProfile=null!;
    }
    [DataContract] private sealed class AuthoredOrdinary {[DataMember] public string name="";[DataMember] public AuthoredRow summary=null!;}
    [DataContract] private sealed class AuthoredRowReport
    { [DataMember] public AuthoredRow summary=null!;[DataMember] public AuthoredOrdinary[] ordinary=null!; }
    private sealed class InputState:IState
    {
        public void Enter<TPayload>(IStateProvider provider,TPayload payload=null!)where TPayload:class { }
        public void Exit<TPayload>(IStateProvider provider,TPayload payload=null!)where TPayload:class { }
    }
    private static void Assign(object owner,string field,object value)=>AccessTools.Field(owner.GetType(),field).SetValue(owner,value);
    private static ExtendedButton NativeButton(RectTransform rect,Graphic graphic,AuthoredRow spec)
    {
        var button=rect.gameObject.AddComponent<ExtendedButton>();button.targetGraphic=graphic;
        button.transition=Selectable.Transition.None;button.navigation=new Navigation{mode=Navigation.Mode.None};
        button.onMouseEnter=new UnityEvent();button.onMouseExit=new UnityEvent();
        button.onSelected=new UnityEvent();button.onDeselected=new UnityEvent();
        button.scaleNonInteractable=spec.scaleNonInteractable;button.highlightScaleFactor=spec.highlightScale;
        button.animateScaling=false;button.animationDuration=spec.animationDuration;button.useAudioController=spec.useAudioController;
        button.audioProfile=spec.audioProfile.Profile();
        button.mouseClickAudioItem=spec.audio.mouseClickAudioItem;button.mouseDownAudioItem=spec.audio.mouseDownAudioItem;
        button.mouseUpAudioItem=spec.audio.mouseUpAudioItem;button.mouseEnterAudioItem=spec.audio.mouseEnterAudioItem;
        button.mouseExitAudioItem=spec.audio.mouseExitAudioItem;button.nonInteractableMouseDownAudioItem=spec.audio.nonInteractableMouseDownAudioItem;
        return button;
    }
    private sealed class MoreRow
    {
        internal readonly RectTransform Root,Holder,Ability,Mini,Full;
        internal readonly Image Ink,Input;
        internal readonly UILevelUpCard Entry;
        internal readonly AbilityCardUI Card;
        internal readonly ExtendedButton Button,InnerButton;
        internal readonly MenuDepthCardPlane Plane;
        internal int Enters,Exits,Clicks,InnerEnters;
        internal MoreRow(Scene scene,AuthoredRow spec,int index)
        {
            Root=Rect("original UILevelUpCard "+index,scene.Root,new Vector2(spec.width,spec.height));
            Root.pivot=spec.pivot;Root.anchoredPosition=new Vector2(spec.width*.5f,130-index*100+spec.height*.5f);
            Root.gameObject.SetActive(false);
            Input=Root.gameObject.AddComponent<Image>();Input.color=new Color(1,1,1,spec.rowAlpha);Input.raycastTarget=spec.rowRaycast;
            Root.gameObject.AddComponent<CanvasGroup>();
            Entry=Root.gameObject.AddComponent<UILevelUpCard>();
            var container=Rect("Container",Root,new Vector2(spec.width,spec.height));container.localPosition=Root.rect.center;
            Holder=Rect("CardHolder",container,container.rect.size);Holder.anchoredPosition=new Vector2(9.29998779296875f,0);
            var readOnly=Holder.gameObject.AddComponent<CanvasGroup>();
            readOnly.blocksRaycasts=spec.holderBlocksRaycasts;readOnly.interactable=spec.holderInteractable;
            Ability=Rect("native pooled AbilityCardUI "+index,Holder,new Vector2(335,55));Ability.SetAsFirstSibling();
            Card=Ability.gameObject.AddComponent<AbilityCardUI>();
            Mini=Rect("native Mini "+index,Ability,new Vector2(335,55));var mini=Mini.gameObject.AddComponent<MiniAbilityCard>();
            Ink=Picture("original mini printed title and background",Mini,Mini.rect.size,Color.yellow);
            Full=Rect("native Full preview "+index,Ability,new Vector2(294,450));var full=Full.gameObject.AddComponent<FullAbilityCard>();
            Picture("original full print",Full,Full.rect.size,Color.green);
            Assign(full,"_enhancementElements",new CardEnhancementElements());
            Assign(Card,"miniAbilityCard",mini);Assign(Card,"fullAbilityCard",full);
            Assign(Card,"mode",CardHandMode.DeckSelection);Assign(Card,"previewCardHeight",55f);
            Assign(Card,"currentFullCardSpacing",33f);Card.MarkAsInFurtherAbilityPanel(true);
            InnerButton=NativeButton(Ability,Ink,spec);Assign(Card,"button",InnerButton);
            InnerButton.onMouseEnter.AddListener(Card.OnPointerEnter);InnerButton.onMouseEnter.AddListener(()=>InnerEnters++);
            InnerButton.onMouseExit.AddListener(Card.OnPointerExit);
            Button=NativeButton(Root,Input,spec);Button.interactable=false;Button.targetRect=container;Button.hoverMovement=spec.hoverMovement;
            Assign(Entry,"button",Button);Assign(Entry,"abilityCard",Card);Assign(Entry,"cardHolder",Holder);
            Assign(Entry,"_fullCardPrewiewOffset",new Vector3(-60,-45,0));
            Button.onMouseEnter.AddListener(Entry.MouseEnter);Button.onMouseExit.AddListener(Entry.MouseExit);
            Button.onClick.AddListener(()=>Clicks++);Entry.OnMouseEnter.AddListener(()=>Enters++);Entry.OnMouseExit.AddListener(()=>Exits++);
            Plane=Mini.gameObject.AddComponent<MenuDepthCardPlane>();Plane.Configure(mini,Mini,new[]{Ink},scene.Root);
            Full.gameObject.SetActive(false);Root.gameObject.SetActive(true);
        }
        internal Vector3 Physical(Vector2 local)=>Mini.TransformPoint(new Vector3(local.x,local.y,0))
            -Mini.forward*(335*.056f*Mini.TransformVector(Vector3.right).magnitude);
    }

    public static IEnumerator RunMoreCards(string output,Action<int> count)
    {
        _checks=0;AudioOutput.Clear();VRHand.Now=0;var native=NativePorts();
        try
        {
            AuthoredRow spec;
            using(var input=File.OpenRead(Environment.GetEnvironmentVariable("GHVR_LASER_NATIVE_ROW")!))
                spec=((AuthoredRowReport)new DataContractJsonSerializer(typeof(AuthoredRowReport)).ReadObject(input)).summary;
            using(var scene=new Scene())
            {
                var navigation=new GameObject("native navigation input").AddComponent<UINavigation>();
                var state=(NavigationStateMachine)FormatterServices.GetUninitializedObject(typeof(NavigationStateMachine));
                var browse=new InputState();var reveal=new InputState();var history=new List<IState>{browse};
                Assign(state,"_states",new Dictionary<Type,Dictionary<Enum,IState>>{[typeof(CampaignMapStateTag)]=new(){[CampaignMapStateTag.ShowingCardLevelUp]=reveal}});
                Assign(state,"_stateHistory",history);Assign(navigation,"<StateMachine>k__BackingField",state);
                var manager=new GameObject("native UI camera input").AddComponent<UIManager>();Assign(manager,"uiCamera",scene.Head);
                var first=new MoreRow(scene,spec,0);var second=new MoreRow(scene,spec,1);
                try
                {
                    Canvas.ForceUpdateCanvases();yield return null;
                    Vector3 physical=first.Physical(Vector2.zero);scene.Aim(physical);
                    var original=new UguiPointer(HandSide.Right,true);
                    Vector2 nativeScreen=RectTransformUtility.WorldToScreenPoint(scene.Head,first.Mini.TransformPoint(Vector3.zero));
                    Check(original.TryRaycast(scene.Canvas,nativeScreen,out var flat)&&flat.gameObject==first.Input.gameObject,
                        "authentic non-raycasting native holder gives flat input to its outer transparent row");
                    Check(first.Plane.TryRaycast(scene.Hand.Ray.Current.Origin,scene.Hand.Ray.Current.Direction,20,out var raised)
                        &&Vector3.Distance(raised.WorldPoint,physical)<.0001f,"More Cards physical centre uses its unchanged independent 5.6 percent card plane");
                    File.WriteAllText(Path.Combine(output,"native-centre.json"),"{\"flat_target\":\""+flat.gameObject.name+"\",\"holder_blocks_raycasts\":false,\"physical_distance\":"+raised.Distance.ToString(System.Globalization.CultureInfo.InvariantCulture)+"}");
                    scene.Tick();
                    Check(first.Enters==1&&first.Full.gameObject.activeSelf&&scene.Driver.Hovered==first.Input.gameObject,
                        "raised More Cards centre reaches the original row MouseEnter callback");
                    Check(!first.Card.IsHovered&&first.InnerEnters==0,"native Further Ability flag keeps its internal card hover inactive");
                    Check(scene.Hand.Pulses.Count==1&&scene.Hand.Pulses[0]==HapticPreset.HoverTick,
                        "cold More Cards preview reaches the real hand sender with the ordinary HoverTick preset");
                    Check(AudioOutput.Count==1&&AudioOutput[0]==spec.audioProfile.mouseEnterAudioItem,
                        "cold More Cards preview plays its exact original native hover sound once");
                    foreach(float yaw in new[]{0f,34f,-29f})
                    {
                        scene.Root.SetPositionAndRotation(new Vector3(.04f,.05f,.03f),Quaternion.Euler(9,yaw,4));scene.Root.localScale=new Vector3(.0022f,.0019f,.0025f);
                        foreach(Vector2 sample in new[]{Vector2.zero,new Vector2(-145,0),new Vector2(145,0),new Vector2(20,12),new Vector2(20,-12)})
                        {
                            physical=first.Physical(sample);scene.Aim(physical);scene.Tick();
                            Check(scene.Driver.HasHit&&Vector3.Distance(scene.Hand.Ray.WorldUiHit,physical)<.0001f,"More Cards centre edges and label retain the physical point through moved rotated scaled hosts");
                            Check(scene.Driver.Hovered==first.Input.gameObject&&first.Enters==1&&first.Full.gameObject.activeSelf,
                                "original row hover stays continuous across More Cards centre edges and label");
                        }
                    }
                    foreach(Vector2 sample in new[]{Vector2.zero,new Vector2(-145,0),new Vector2(145,0),new Vector2(20,12)})
                    {
                        scene.Driver.Cancel();int before=first.Enters;scene.Aim(first.Physical(sample));scene.Tick();
                        Check(first.Enters==before+1&&scene.Driver.Hovered==first.Input.gameObject&&first.Full.gameObject.activeSelf,
                            "a fresh More Cards centre edge or label hover invokes the original row exactly once");
                    }
                    int exits=first.Exits;scene.Aim(second.Physical(Vector2.zero));scene.Tick();
                    Check(first.Exits==exits+1&&second.Enters==1&&!first.Full.gameObject.activeSelf&&second.Full.gameObject.activeSelf,
                        "moving directly between native More Cards rows switches original preview and balanced callbacks immediately");
                    scene.Driver.Cancel();history[0]=reveal;int enters=second.Enters;
                    scene.Aim(second.Physical(Vector2.zero));scene.Tick();
                    Check(second.Enters==enters&&!second.Full.gameObject.activeSelf,"original ShowingCardLevelUp state denies browsing preview even with a valid raised hit");
                    scene.Driver.Cancel();history[0]=browse;
                    second.Button.enabled=false;scene.Tick();
                    Check(second.Enters==enters,"disabled original row button receives no invented hover callback");
                    scene.Driver.Cancel();second.Button.enabled=true;second.Button.interactable=false;scene.Tick();
                    Check(second.Enters==enters+1&&second.Full.gameObject.activeSelf,"locked non-interactable original rows still allow native preview hover");
                    scene.Hand.TriggerDown=scene.Hand.TriggerPressed=true;scene.Tick();scene.Hand.TriggerDown=false;scene.Hand.TriggerPressed=false;scene.Tick();
                    Check(second.Clicks==0,"locked native row preview never converts hover permission into click permission");
                    scene.Driver.Cancel();
                    var rowGroup=second.Root.GetComponent<CanvasGroup>();rowGroup.blocksRaycasts=false;enters=second.Enters;scene.Tick();
                    Check(scene.Driver.HasHit&&scene.Driver.Hovered==null&&second.Enters==enters,"read-only original row retains raised occlusion without receiving events or clicking through");
                    rowGroup.blocksRaycasts=true;scene.Root.GetComponent<GraphicRaycaster>().enabled=false;scene.Tick();
                    Check(second.Enters==enters&&scene.Driver.Hovered==null,"disabled host raycaster still denies native More Cards input");
                    scene.Root.GetComponent<GraphicRaycaster>().enabled=true;
                    second.Root.gameObject.SetActive(false);scene.Tick();
                    Check(!second.Plane.IsReady&&second.Enters==enters,"hidden original More Cards row has no raised source or callback");
                    second.Root.gameObject.SetActive(true);
                    // Card admission and completed head capture are explicit
                    // fixture boundaries; their coordinators run in the card
                    // suite. Reuse the real rebind and capture lease after a
                    // native pool reactivation, then restore its original pose.
                    second.Plane.Rebind(scene.Root,new[]{second.Ink});Canvas.ForceUpdateCanvases();
                    second.Plane.BeforeCapture(new MenuDepthEye(false,scene.Head.transform.position,scene.Head));second.Plane.AfterCapture();
                    Canvas.ForceUpdateCanvases();
                    scene.Aim(second.Physical(Vector2.zero));
                    Check(MenuDepthSources.TryRaycast(scene.Canvas,scene.Hand.Ray.Current.Origin,scene.Hand.Ray.Current.Direction,20,out _),
                        "original card readmission restores its real printed-surface ownership before mask and Canvas transitions");
                    var aperture=Rect("native More Cards clip",scene.Root,new Vector2(20,20));aperture.position=second.Mini.position;aperture.gameObject.AddComponent<RectMask2D>();
                    second.Root.SetParent(aperture,true);Canvas.ForceUpdateCanvases();scene.Aim(second.Physical(new Vector2(100,0)));scene.Tick();
                    Check(!MenuDepthSources.TryRaycast(scene.Canvas,scene.Hand.Ray.Current.Origin,scene.Hand.Ray.Current.Direction,20,out _),
                        "native More Cards mask clips its raised printed surface at the original input pixel");
                    second.Root.SetParent(scene.Root,true);Object.DestroyImmediate(aperture.gameObject);Canvas.ForceUpdateCanvases();
                    // A real adopted nested Canvas must not strand the outer native
                    // input owner on a different original GraphicRaycaster.
                    var nested=second.Ability.gameObject.AddComponent<Canvas>();nested.overrideSorting=false;nested.sortingOrder=101;nested.worldCamera=scene.Head;
                    nested.gameObject.AddComponent<GraphicRaycaster>();UguiPokeSurfaces.Nested[scene.Canvas]=new(){nested};
                    Canvas.ForceUpdateCanvases();yield return null;scene.Driver.Cancel();enters=second.Enters;
                    scene.Aim(second.Physical(Vector2.zero));scene.Tick();
                    var nativeHits=new List<RaycastResult>();var probeData=new PointerEventData(EventSystem.current)
                        {position=RectTransformUtility.WorldToScreenPoint(scene.Head,second.Mini.position)};
                    scene.Canvas.GetComponent<GraphicRaycaster>().Raycast(probeData,nativeHits);
                    File.WriteAllText(Path.Combine(output,"adopted-canvas-probe.txt"),"enters="+enters+"->"+second.Enters+" hovered="+(scene.Driver.Hovered!=null?scene.Driver.Hovered.name:"null")
                        +" physical="+scene.Driver.HasHit+" visualCanvas="+second.Ink.canvas.name+" inputCanvas="+second.Input.canvas.name+" inputCull="+second.Input.canvasRenderer.cull
                        +" inkCull="+second.Ink.canvasRenderer.cull+" sourceReady="+second.Plane.IsReady+" directPlane="+second.Plane.TryRaycast(scene.Hand.Ray.Current.Origin,scene.Hand.Ray.Current.Direction,20,out _)
                        +" registeredSource="+MenuDepthSources.TryRaycast(scene.Canvas,scene.Hand.Ray.Current.Origin,scene.Hand.Ray.Current.Direction,20,out _)
                        +" inkAlpha="+second.Ink.canvasRenderer.GetInheritedAlpha()+" canvasEnabled="+nested.isActiveAndEnabled
                        +" nativeHits="+string.Join(",",nativeHits.ConvertAll(hit=>hit.gameObject.name))+"\n");
                    Check(second.Enters==enters+1&&scene.Driver.Hovered==second.Input.gameObject,"adopted nested mini Canvas still reaches its original outer More Cards row raycaster");
                    Check(second.Plane.TryRaycast(scene.Hand.Ray.Current.Origin,scene.Hand.Ray.Current.Direction,20,out raised)&&raised.NativeRoot==second.Button.transform,
                        "More Cards input scope is exactly its typed native selectable owner");
                    foreach(bool locked in new[]{false,true})
                    {
                        scene.Driver.Cancel();second.Button.interactable=!locked;enters=second.Enters;
                        scene.Aim(second.Physical(Vector2.zero));scene.Tick();
                        Check(second.Enters==enters+1&&second.Full.gameObject.activeSelf==locked,
                            "normal and locked adopted rows execute their original preview eligibility on the first centre hover");
                        foreach(Vector2 sample in new[]{Vector2.zero,new Vector2(-145,0),new Vector2(145,0),new Vector2(20,12),new Vector2(20,-12)})
                        {
                            physical=second.Physical(sample);scene.Aim(physical);scene.Tick();
                            Check(scene.Driver.Hovered==second.Input.gameObject&&second.Enters==enters+1
                                &&Vector3.Distance(scene.Hand.Ray.WorldUiHit,physical)<.0001f,
                                "adopted normal and locked row centre edges and label keep native hover and physical laser geometry");
                        }
                        int clicks=second.Clicks;scene.Hand.TriggerDown=scene.Hand.TriggerPressed=true;scene.Tick();
                        scene.Hand.TriggerDown=false;scene.Hand.TriggerPressed=false;scene.Tick();
                        Check(second.Clicks==clicks+(locked?0:1),"adopted native row press and release retain original locked and unlocked click permission");
                    }
                    scene.Driver.Cancel();enters=second.Enters;
                    var poke=second.Root.gameObject.AddComponent<PokeOnlyTarget>();scene.Tick();
                    Check(scene.Driver.Hovered==null&&second.Enters==enters,"native row ownership never bypasses far-ray poke-only exclusion");
                    Object.DestroyImmediate(poke);
                    // Keep both original raycaster results: opening the native
                    // holder's input demonstrates a legitimate nested child hit.
                    var holderGroup=second.Holder.GetComponent<CanvasGroup>();holderGroup.blocksRaycasts=true;holderGroup.interactable=true;
                    Check(second.Plane.TryRaycast(scene.Hand.Ray.Current.Origin,scene.Hand.Ray.Current.Direction,20,out var childHit),
                        "legitimate nested child probe retains the independently established physical surface");
                    nativeScreen=RectTransformUtility.WorldToScreenPoint(scene.Head,childHit.NativePoint);
                    bool flatChild=original.TryRaycast(scene.Canvas,nativeScreen,out var flatTop);
                    bool raisedChild=original.TryRaycastSurface(scene.Canvas,nativeScreen,childHit.Graphic,childHit.PreferGraphic,childHit.WorldPoint,childHit.Distance,out var raisedTop,childHit.NativeRoot);
                    File.WriteAllText(Path.Combine(output,"native-child-priority.txt"),"flat="+(flatChild?flatTop.gameObject.name:"null")+" order="+flatTop.sortingOrder+" depth="+flatTop.depth
                        +" raised="+(raisedChild?raisedTop.gameObject.name:"null")+" order="+raisedTop.sortingOrder+" depth="+raisedTop.depth+"\n");
                    Check(flatChild&&flatTop.gameObject==second.Ink.gameObject,"original unmodified flat query gives legitimate nested mini child its native input priority");
                    Check(flatTop.sortingOrder==scene.Canvas.sortingOrder,
                        "independent original nested child and host expose equal native sorting orders");
                    Check(raisedChild&&raisedTop.gameObject==flatTop.gameObject,
                        "raised native query preserves the original flat top hit for equal-order nested children");
                    scene.Tick();
                    Check(scene.Driver.Hovered==second.Ink.gameObject&&second.InnerEnters>0&&!second.Card.IsHovered,
                        "original nested child graphics and internal native callbacks retain their native priority within the row");
                    scene.Driver.Cancel();holderGroup.blocksRaycasts=false;holderGroup.interactable=false;
                    nested.overrideSorting=true;Canvas.ForceUpdateCanvases();yield return null;Canvas.ForceUpdateCanvases();
                    scene.Aim(second.Physical(Vector2.zero));
                    Check(second.Plane.TryRaycast(scene.Hand.Ray.Current.Origin,scene.Hand.Ray.Current.Direction,20,out var overrideHit),
                        "native override-sorting transition retains its actual printed source before the input comparison");
                    nativeScreen=RectTransformUtility.WorldToScreenPoint(scene.Head,overrideHit.NativePoint);
                    Check(original.TryRaycast(scene.Canvas,nativeScreen,out var overrideTop),
                        "original flat query independently supplies the override-sorting transition input owner");
                    File.WriteAllText(Path.Combine(output,"override-sorting-native-owner.txt"),overrideTop.gameObject.name+" order="+overrideTop.sortingOrder+" depth="+overrideTop.depth+"\n");
                    scene.Tick();
                    Check(scene.Driver.Hovered==overrideTop.gameObject&&second.Full.gameObject.activeSelf,
                        "native override-sorting transition retains original flat top-hit permission and row callbacks");
                    scene.Driver.Cancel();nested.overrideSorting=false;
                    var ownerRect=Rect("adopted original row owner Canvas",scene.Root,new Vector2(500,500));
                    var ownerCanvas=ownerRect.gameObject.AddComponent<Canvas>();ownerCanvas.overrideSorting=true;ownerCanvas.sortingOrder=100;
                    ownerCanvas.worldCamera=scene.Head;
                    var ownerRaycaster=ownerRect.gameObject.AddComponent<GraphicRaycaster>();
                    second.Root.SetParent(ownerRect,true);UguiPokeSurfaces.Nested[scene.Canvas].Add(ownerCanvas);
                    Canvas.ForceUpdateCanvases();yield return null;scene.Aim(second.Physical(Vector2.zero));scene.Tick();
                    Check(scene.Driver.Hovered==second.Input.gameObject,"registered original nested owner Canvas retains native row input");
                    scene.Driver.Cancel();enters=second.Enters;
                    UguiPokeSurfaces.Nested[scene.Canvas].Remove(ownerCanvas);scene.Tick();
                    Check(scene.Driver.Hovered==null&&second.Enters==enters,"unregistered original owner Canvas cannot gain permission through a registered visual Canvas");
                    UguiPokeSurfaces.Nested[scene.Canvas].Add(ownerCanvas);ownerRaycaster.enabled=false;scene.Tick();
                    Check(scene.Driver.Hovered==null&&second.Enters==enters,"disabled original owner raycaster cannot gain permission through physical mini geometry");
                    ownerRaycaster.enabled=true;ownerCanvas.enabled=false;scene.Tick();
                    Check(scene.Driver.Hovered==null&&second.Enters==enters,"inactive original owner Canvas cannot receive raised row events");
                    ownerCanvas.enabled=true;rowGroup.blocksRaycasts=false;scene.Tick();
                    Check(scene.Driver.HasHit&&scene.Driver.Hovered==null&&second.Enters==enters,"adopted native owner Canvas still respects original row CanvasGroup read-only input");
                    rowGroup.blocksRaycasts=true;nested.enabled=false;scene.Tick();
                    Check(scene.Driver.Hovered==null&&second.Enters==enters,"inactive original visual Canvas cannot authorize the outer native row");
                    nested.enabled=true;UguiPokeSurfaces.Nested[scene.Canvas].Remove(nested);scene.Tick();
                    Check(scene.Driver.Hovered==null&&second.Enters==enters,"unregistered original visual Canvas cannot authorize the outer native row");
                    UguiPokeSurfaces.Nested[scene.Canvas].Add(nested);second.Button.enabled=false;scene.Tick();
                    Check(second.Enters==enters,"adopted disabled native row component receives no hover callback");
                    scene.Driver.Cancel();second.Button.enabled=true;
                    Canvas.ForceUpdateCanvases();yield return null;Canvas.ForceUpdateCanvases();scene.Aim(second.Physical(Vector2.zero));
                    Check(MenuDepthSources.TryRaycast(scene.Canvas,scene.Hand.Ray.Current.Origin,scene.Hand.Ray.Current.Direction,20,out var recoveryHit),
                        "reenabled adopted native row retains its physical source before input recovery");
                    nativeScreen=RectTransformUtility.WorldToScreenPoint(scene.Head,recoveryHit.NativePoint);
                    Check(original.TryRaycast(scene.Canvas,nativeScreen,out var recoveryTop)&&recoveryTop.gameObject==second.Input.gameObject,
                        "original flat raycaster independently confirms the adopted row is ready after reenabling");
                    scene.Tick();
                    Check(second.Enters==enters+1&&second.Full.gameObject.activeSelf,"reenabled adopted locked row recovers its original preview callback; enters="
                        +enters+"->"+second.Enters+" preview="+second.Full.gameObject.activeSelf+" hovered="+(scene.Driver.Hovered!=null?scene.Driver.Hovered.name:"null"));
                    scene.Driver.Cancel();
                    var nestedMask=Rect("native adopted row mask",ownerRect,new Vector2(20,20));nestedMask.position=second.Mini.position;
                    nestedMask.gameObject.AddComponent<RectMask2D>();second.Root.SetParent(nestedMask,true);Canvas.ForceUpdateCanvases();
                    scene.Aim(second.Physical(new Vector2(100,0)));scene.Tick();
                    Check(!MenuDepthSources.TryRaycast(scene.Canvas,scene.Hand.Ray.Current.Origin,scene.Hand.Ray.Current.Direction,20,out _)
                        &&scene.Driver.Hovered==null,"adopted row ownership retains original native mask clipping for geometry and callbacks");
                    second.Root.SetParent(ownerRect,true);Object.DestroyImmediate(nestedMask.gameObject);Canvas.ForceUpdateCanvases();
                    // A detached full preview must keep its own native scope even
                    // when its original AbilityCardUI still belongs to this row.
                    scene.Driver.Cancel();second.Full.SetParent(scene.Root,false);second.Full.localPosition=new Vector3(-280,-150,0);second.Full.gameObject.SetActive(true);
                    var fullCanvas=second.Full.GetComponent<Canvas>();if(fullCanvas!=null)Object.DestroyImmediate(fullCanvas);
                    var fullPlane=second.Full.gameObject.AddComponent<MenuDepthCardPlane>();var fullInk=second.Full.GetComponentInChildren<Image>();
                    fullPlane.Configure(second.Full.GetComponent<FullAbilityCard>(),second.Full,new[]{fullInk},scene.Root);Canvas.ForceUpdateCanvases();
                    Vector3 fullPoint=second.Full.position-second.Full.forward*(294*.104f*second.Full.TransformVector(Vector3.right).magnitude);scene.Aim(fullPoint);
                    Check(fullPlane.TryRaycast(scene.Hand.Ray.Current.Origin,scene.Hand.Ray.Current.Direction,20,out raised)&&raised.NativeRoot==second.Full,
                        "detached native full preview retains its original card-only input root");
                    var ordinary=Rect("ordinary native ability overview",scene.Root,new Vector2(335,55));ordinary.localPosition=new Vector3(-220,200,0);
                    var ordinaryAbility=ordinary.gameObject.AddComponent<AbilityCardUI>();
                    var ordinaryMini=Rect("ordinary native mini",ordinary,new Vector2(335,55));var ordinaryOwner=ordinaryMini.gameObject.AddComponent<MiniAbilityCard>();
                    Assign(ordinaryAbility,"miniAbilityCard",ordinaryOwner);var ordinaryInk=Picture("ordinary mini print",ordinaryMini,ordinaryMini.rect.size,Color.white);
                    var ordinaryPlane=ordinaryMini.gameObject.AddComponent<MenuDepthCardPlane>();ordinaryPlane.Configure(ordinaryOwner,ordinaryMini,new[]{ordinaryInk},scene.Root);
                    Canvas.ForceUpdateCanvases();scene.Aim(ordinaryMini.position-ordinaryMini.forward*(335*.056f*ordinaryMini.TransformVector(Vector3.right).magnitude));
                    Check(ordinaryPlane.TryRaycast(scene.Hand.Ray.Current.Origin,scene.Hand.Ray.Current.Direction,20,out raised)&&raised.NativeRoot==ordinaryMini,
                        "ordinary ability mini cards outside More Cards retain their original card-only input root");
                    File.WriteAllText(Path.Combine(output,"native-callback-scope.txt"),"Actual GH.Runtime ExtendedButton enter/exit/down/up/click, OnHighlight, UILevelUpCard.MouseEnter/MouseExit, AbilityCardUI.OnPointerEnter and ToggleFullCardPreview execute. Native authored row/Image/blocked CardHolder values are independent read-only asset inputs. Campaign bootstrap, SetBackground/SetDisplayMode artwork, burning effects and desktop fit are explicit ports; highlight animation uses its original immediate path. No game boot, D3D11 or HMD claim.\n");
                }
                finally {scene.Driver.Cancel();Object.DestroyImmediate(navigation.gameObject);Object.DestroyImmediate(manager.gameObject);}
            }
            count(_checks);
        }
        finally {native.UnpatchAll(native.Id);}
    }
}
