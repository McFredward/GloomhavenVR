using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEditor;
using Object=UnityEngine.Object;

/// <summary>Real native serialized widgets + the complete built production DLL. No layout,
/// protocol, clone, clip, or playback implementation is reproduced by this fixture.</summary>
public sealed class RulesHarness:MonoBehaviour
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    readonly List<string> checks=new List<string>(); int controls; string output;
    Assembly mod,game; object fold,panel,mirror,player,clock,vrPointer; RectTransform content,host,mount,remoteMount;
    GameObject nativeManager; GameObject prefab; Camera camera; object previousManager;
    object columnTray, previousTray, ruleSurface, objectiveSurface, elementSurface, objectivePanel, elementPanel; GameObject goal;
    Type Type(string name)=>mod.GetType(name,true);
    object Get(object obj,string name){var type=obj as Type??obj.GetType();for(;type!=null;type=type.BaseType){var f=type.GetField(name,All);if(f!=null)return f.GetValue(obj is Type?null:obj);var p=type.GetProperty(name,All);if(p!=null)return p.GetValue(obj is Type?null:obj);}throw new Exception("Missing field "+name);}
    void Set(object obj,string name,object value){var type=obj as Type??obj.GetType();for(;type!=null;type=type.BaseType){var f=type.GetField(name,All);if(f!=null){f.SetValue(obj is Type?null:obj,value);return;}var p=type.GetProperty(name,All);if(p!=null){p.SetValue(obj is Type?null:obj,value);return;}}throw new Exception("Missing field "+name);}
    object Call(object obj,string name,params object[] args){var type=obj as Type??obj.GetType();var methods=type.GetMethods(All).Where(m=>m.Name==name&&m.GetParameters().Length>=args.Length);foreach(var method in methods){var p=method.GetParameters();if(p.Take(args.Length).Where((x,i)=>args[i]!=null&&!x.ParameterType.IsInstanceOfType(args[i])&&!x.ParameterType.IsByRef).Any())continue;if(p.Skip(args.Length).Any(x=>!x.IsOptional))continue;var values=new object[p.Length];Array.Copy(args,values,args.Length);for(int i=args.Length;i<p.Length;i++)values[i]=p[i].DefaultValue;try{return method.Invoke(obj is Type?null:obj,values);}catch(TargetInvocationException e){throw new Exception("Production invocation: "+type+"."+name,e.InnerException);}}throw new Exception("Missing method "+type+"."+name);}
    object New(string name,params object[] args)=>Activator.CreateInstance(Type(name),BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,args,null);
    void Check(bool value,string text){if(!value)throw new Exception(text);checks.Add(text);}
    void Control(bool rejected,string text){Check(rejected,"NEGATIVE CONTROL: "+text);controls++;}
    IEnumerator Start(){output=Argument("-evidenceRoot");mod=Assembly.Load("GloomhavenVR");game=Assembly.Load("GH.Runtime");IEnumerator work=Run();while(true){object next=null;bool more=false;try{more=work.MoveNext();if(more)next=work.Current;}catch(Exception error){Finish(false,error.ToString());yield break;}if(!more)break;yield return next;}Finish(true,null);}
    IEnumerator Run()
    {
        // Logging initialized exactly as a plugin would, avoiding unrelated native gameplay Awake.
        var logType=Assembly.Load("BepInEx").GetType("BepInEx.Logging.ManualLogSource",true);
        Call(Type("GloomhavenVR.Core.VRLog"),"Init",Activator.CreateInstance(logType,new object[]{"Rules proof"}));
        Type paths=Assembly.Load("BepInEx").GetType("BepInEx.Paths",true);
        Call(paths,"SetExecutablePath",Path.Combine(output,"Gloomhaven.exe"));Directory.CreateDirectory((string)Get(paths,"ConfigPath"));
        Call(Type("GloomhavenVR.WorldUI.WorldUIConfig"),"Bind");
        Call(Type("GloomhavenVR.WorldUI.ButtonTuning"),"Bind");
        object loaderInputs=Call(Type("GloomhavenVR.Core.ModuleConfig"),"Create","rules-proof-loader");
        var bindBool=loaderInputs.GetType().GetMethods(All).Single(m=>m.Name=="Bind"&&m.IsGenericMethodDefinition&&m.GetParameters().Length==4&&m.GetParameters()[0].ParameterType==typeof(string)&&m.GetParameters()[3].ParameterType==typeof(string));
        Set(Type("GloomhavenVR.Plugin"),"DevMode",bindBool.MakeGenericMethod(typeof(bool)).Invoke(loaderInputs,new object[]{"Proof","DevMode",false,"Actual loader state: map has no live VR session"}));
        Set(Type("GloomhavenVR.Plugin"),"ScrollWithStickOnly",bindBool.MakeGenericMethod(typeof(bool)).Invoke(loaderInputs,new object[]{"Proof","ScrollWithStickOnly",Get(Type("GloomhavenVR.Defaults"),"ScrollWithStickOnly"),"Actual native pointer input setting"}));
        prefab=Resources.Load<GameObject>("NativeRule");Check(prefab!=null,"original serialized native rule prefab loaded");
        var font=Resources.Load<TMP_FontAsset>("NativeRulesFont");Check(font.characterTable.Count>700&&font.atlasTextures[0].width==2048,"native font glyph tables and lossless original atlas recovered");
        Set(Type("GloomhavenVR.WorldUI.NativeButtonSkin"),"_sampled",true);
        Set(Type("GloomhavenVR.WorldUI.NativeButtonSkin"),"_font",font);
        Set(Type("GloomhavenVR.WorldUI.NativeButtonSkin"),"_normalSprite",Resources.Load<Sprite>("NativeButton"));
        camera=new GameObject("Evidence camera").AddComponent<Camera>();camera.gameObject.tag="MainCamera";camera.orthographic=true;camera.orthographicSize=.65f;camera.backgroundColor=new Color(.07f,.075f,.085f);camera.clearFlags=CameraClearFlags.SolidColor;camera.transform.position=new Vector3(.15f,-.11f,-2f);
        // Native laser/finger events below are delivered through the real EventSystem and
        // GraphicRaycaster. A desktop StandaloneInputModule would inject unrelated host mouse
        // exits while the fixture holds the VR pointer over this world-space preview.
        new GameObject("Events",typeof(EventSystem));
        GameObject home=new GameObject("Native original canvas",typeof(RectTransform),typeof(Canvas));home.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;
        content=(RectTransform)new GameObject("Scenario Modifier Container",typeof(RectTransform),typeof(VerticalLayoutGroup)).transform;content.SetParent(home.transform,false);
        NativeFixture.ApplyRect(content,File.ReadAllText("Assets/NativeSource/rect.json"));
        JsonUtility.FromJsonOverwrite(File.ReadAllText("Assets/NativeSource/layout.json"),content.GetComponent<VerticalLayoutGroup>());
        var nativeContainer=content.gameObject.AddComponent(game.GetType("ScenarioModifierContainer",true));Set(nativeContainer,"ScenarioModifierPrefab",prefab);
        nativeManager=new GameObject("Dormant native manager");nativeManager.SetActive(false);var manager=nativeManager.AddComponent(game.GetType("UIManager",true));
        previousManager=Get(manager.GetType(),"Instance");Set(manager.GetType(),"Instance",manager);Set(manager,"scenarioModifierContainer",nativeContainer);
        string[] rules={
            "Die untere Tür wird durch Erbeuten einer verzierten Truhe aufgesperrt. Die obere Tür wird durch Erbeuten von zwei verzierten Truhen aufgesperrt. Die untere Tür zum Ausgang wird durch Erbeuten von drei verzierten Truhen aufgesperrt.",
            "Starke Winde zwingen Söldner und Söldner-Beschworene, sich zu Beginn jeder Runde ein Feld in eine andere Richtung zu bewegen. Die Richtung ändert sich im Uhrzeigersinn.",
            "Alle Speidraken beginnen schlafend zZ, und handeln erst, wenn sie aufgeweckt werden. Dies geschieht, wenn sie angegriffen werden, Schaden erleiden, von einem negativen Zustand betroffen sind oder eine Bewegung in einem angrenzenden Feld endet - auch, wenn dies durch starke Winde geschieht."};
        Rows(rules);yield return null;
        // This invokes the real conversion, registers the actual laser/finger canvas and preserves
        // native target ownership; no host or conversion algorithm is substituted.
        panel=Call(Type("GloomhavenVR.WorldUI.CanvasConversion"),"Convert",content,"RulesProof",true,null,false);
        Check(panel!=null,"production CanvasConversion adopts original native container");host=(RectTransform)Get(panel,"HostRect");
        mount=(RectTransform)new GameObject("Actual board objective mount",typeof(RectTransform)).transform;mount.position=Vector3.zero;
        host.position=mount.position;host.localScale=Vector3.one/1440f;
        fold=New("GloomhavenVR.WorldUI.Surfaces.BoardRulesFoldout",host,content,true);
        Set(Type("GloomhavenVR.WorldUI.Surfaces.ScenarioRulesSurface"),"Presentation",fold);Set(Type("GloomhavenVR.WorldUI.Surfaces.ScenarioRulesSurface"),"PresentationMount",mount);
        for(int i=0;i<3;i++){Call(fold,"Tick",.215f,.016f);yield return new WaitForSeconds(.13f);}
        Check((bool)Get(fold,"Overflow")&&!(bool)Get(fold,"Expanded"),"long original rules keep a visible compact preview at the original column");
        Check(content.gameObject.activeInHierarchy,"collapsed original native widget stays alive; no lifecycle continuation suppression");
        var viewport=(RectTransform)host.Find("GloomhavenVR.RulesFoldout/Viewport");
        Vector3[] corners=new Vector3[4];viewport.GetWorldCorners(corners);
        Check(host.InverseTransformPoint(corners[2]).magnitude<.01f,"preview top-right stays at the exact original rules origin");
        var button=viewport.GetComponent<Button>();
        Check(button.transition==Selectable.Transition.None,"inline input has no viewer-local Selectable transition");
        var eventData=new PointerEventData(EventSystem.current);eventData.position=RectTransformUtility.WorldToScreenPoint(camera,viewport.TransformPoint(viewport.rect.center));
        host.GetComponent<Canvas>().worldCamera=camera;
        var hits=new List<RaycastResult>();((GraphicRaycaster)Get(panel,"HostRaycaster")).Raycast(eventData,hits);
        Check(hits.Any(h=>h.gameObject==button.gameObject),"actual registered host GraphicRaycaster hits the original inline prose target");
        Canvas.ForceUpdateCanvases();TMP_Text first=content.GetChild(0).GetComponent<TMP_Text>();first.ForceMeshUpdate();
        Check((string)Call(fold,"OriginalText",first)==rules[0]&&first.font==font&&first.fontSize==16,"preview preserves complete native source prose and original font/style");
        Check(content.GetComponentsInChildren<TMP_Text>().Any(HasVisibleEllipsis),"native compact preview visibly ends in an ellipsis instead of disappearing");
        BuildColumnProof(font);ColumnProof();
        object closed=Sample();int threeRowBytes=EncodedBytes(closed);float initialHeight=((float[])Get(closed,"Frame"))[4];
        Check(initialHeight>30f&&initialHeight<=.09f*1440f+.1f,"collapsed rules retain visible native lines within the actual preview budget");
        var remoteBoard=new GameObject("Actual remote board root").transform;remoteBoard.position=new Vector3(1f,0,0);
        remoteMount=(RectTransform)new GameObject("Remote objective mount",typeof(RectTransform)).transform;remoteMount.SetParent(remoteBoard,false);
        var layout=Type("GloomhavenVR.Net.RemoteWidgetMirror+LayoutOwner");mirror=NewMirror(layout);
        Call(mirror,"Refresh",content);Check(Get(mirror,"RulesContent")!=null,"original native widget cloned through production neutralization before owner geometry");player=New("GloomhavenVR.Net.RemoteBoardRulesPlayer");clock=New("GloomhavenVR.Net.NativeBoardPresentationClock");
        Apply(closed);Check((bool)Call(mirror,"Refresh",content),"first collapsed owner frame establishes a valid visible original preview on peer");
        Check(!(bool)Get(Get(player,"_foldout"),"Expanded"),"initial compact owner remains compact on peer");Render("long-collapsed-owner-remote.png");PixelParity("native-visible-preview");
        float previewAlpha=viewport.GetComponent<CanvasGroup>().alpha;viewport.GetComponent<CanvasGroup>().alpha=0f;
        Vector3 savedEye=camera.transform.position;Transform peerHost=(Transform)Get(mirror,"RulesHost");peerHost.gameObject.SetActive(false);camera.transform.position=new Vector3(-.3f,-.11f,-2f);
        Color32[] erased=Pixels();Color32 empty=erased[0];Control(erased.Count(c=>Distance(c,empty)>6)<200,"Build610-style hidden native preview is rejected by actual rendered glyph evidence");
        viewport.GetComponent<CanvasGroup>().alpha=previewAlpha;
        Vector3 actualScale=host.localScale;host.localScale=actualScale*.00035f;Color32[] tiny=Pixels();Color32 tinyBackground=tiny[0];
        Control(tiny.Count(c=>Distance(c,tinyBackground)>6)<200,"Build610 double-density placement is rejected by actual native rendered glyph evidence");
        host.localScale=actualScale;peerHost.gameObject.SetActive(true);camera.transform.position=savedEye;
        Check(Vector3.Distance(host.position,((RectTransform)Get(mirror,"RulesHost")).position)>.9f,"separate board parent poses preserve owner and peer placement");
        Set(panel,"MrVisualRoot",Get(fold,"VisualRoot"));Rect collapsedInk=Ink(panel);
        Check(collapsedInk.height>30f&&collapsedInk.height<=initialHeight+2f,"actual MR bounds include native preview glyphs without hidden full prose");
        vrPointer=New("GloomhavenVR.Hands.Interact.UguiPointer",Enum.Parse(Type("GloomhavenVR.Hands.HandSide"),"Right"),true);
        object[] picked={host.GetComponent<Canvas>(),eventData.position,new RaycastResult()};
        Check((bool)vrPointer.GetType().GetMethod("TryRaycast",All).Invoke(vrPointer,picked),"actual VR laser pointer resolves the converted native preview");
        var originalHit=(RaycastResult)picked[2];Check(originalHit.gameObject==button.gameObject,"actual VR laser returns the original inline input target");
        Call(vrPointer,"SetHovered",originalHit.gameObject);
        Check((bool)Get(fold,"Expanded"),"actual pointer hover alone opens the complete inline rules");
        object state=closed;float previousHeight=initialHeight;
        for(int i=0;i<7;i++)
        {
            yield return null;Call(fold,"Tick",.215f,.025f);Canvas.ForceUpdateCanvases();ColumnProof();state=Sample();Apply(state);
            var remoteFold=Get(player,"_foldout");Check(remoteFold!=null,"remote original inline rules bind successfully");
            object remoteState=New("GloomhavenVR.Net.NativeBoardRulesState");Call(remoteFold,"CaptureFrame",remoteState);
            float[] a=(float[])Get(state,"Frame"),b=(float[])Get(remoteState,"Frame");
            Check(Mathf.Abs(a[4]-b[4])<.001f&&Mathf.Abs(a[12]-b[12])<.00001f,"owner-authored inline height and occupied column exactly match remote intermediate "+i);
            Check(a[4]>=previousHeight&&Mathf.Abs(a[3]-.215f*1440f)<.1f,"native expansion grows downward in the same column without horizontal displacement "+i+" (height="+a[4]+", previous="+previousHeight+", width="+a[3]+", expanded="+Get(fold,"Expanded")+")");
            Check(Mathf.Abs(a[12]*1440f-a[4])<.001f,"every intermediate occupied height clears all content below "+i);previousHeight=a[4];
            var remoteText=((RectTransform)Get(mirror,"RulesContent")).GetComponentsInChildren<TMP_Text>(true);
            Check(remoteText.Length==3&&remoteText[0].text==content.GetChild(0).GetComponent<TMP_Text>().text&&remoteText[0].font==font&&remoteText[0].fontSize==16,"remote retains exact original full text/font/native16px size");
            Render("long-intermediate-"+i+"-owner-remote.png");PixelParity("inline-intermediate-"+i);
        }
        Check((float)Get(fold,"OccupiedMeters")>.09f,"expanded full native prose reserves its actual enlarged vertical column");
        Check(((float[])Get(state,"Frame"))[10]==1f/1440f,"native glyph density remains fixed during expansion");
        Rect openInk=Ink(panel);Check(openInk.width<=.215f*1440f+2f&&openInk.height>collapsedInk.height,"actual MR bounds grow vertically inside the unchanged original prose column");
        Check(Mathf.Abs(((float[])Get(state,"Frame"))[4]-((float[])Get(state,"Frame"))[6])<.1f,"the completed inline expansion exposes the entire measured native prose");
        Call(vrPointer,"SetHovered",null,true);
        Check(!(bool)Get(fold,"Expanded"),"temporary pointer expansion closes on exit without a click");
        float fullHeight=((float[])Get(state,"Frame"))[4];
        for(int i=0;i<5;i++){yield return null;Call(fold,"Tick",.215f,.025f);ColumnProof();state=Sample();Apply(state);Check(((float[])Get(state,"Frame"))[4]<=fullHeight,"closing inline height is monotonic "+i);fullHeight=((float[])Get(state,"Frame"))[4];}
        ClickRules(button,eventData);
        Check((bool)Get(fold,"Expanded"),"actual pointer click pins inline rules without hover");
        Call(vrPointer,"SetHovered",null,true);
        Check((bool)Get(fold,"Expanded"),"click-pinned rules remain expanded after pointer leaves");
        ClickRules(button,eventData);
        Check(!(bool)Get(fold,"Expanded"),"a second click unpins rules and returns to compact preview");
        // Deliberately disturb a receiver's native layout: only captured owner geometry may win.
        RectTransform remoteContent=(RectTransform)Get(mirror,"RulesContent");var group=remoteContent.GetComponent<VerticalLayoutGroup>();
        group.padding=new RectOffset(900,900,900,900);Canvas.ForceUpdateCanvases();
        Check(!group.enabled&&remoteContent.GetChild(0).GetComponent<RectTransform>().sizeDelta==content.GetChild(0).GetComponent<RectTransform>().sizeDelta,"disabled receiver layout cannot override original owner row geometry");
        group.enabled=true;LayoutRebuilder.ForceRebuildLayoutImmediate(remoteContent);
        Control(remoteContent.GetChild(0).GetComponent<RectTransform>().sizeDelta!=content.GetChild(0).GetComponent<RectTransform>().sizeDelta,"re-enabled competing native layout breaks owner geometry");group.enabled=false;Apply(Sample());
        host.gameObject.SetActive(false);Call(mirror,"Refresh",content);Apply(state);
        Check(((RectTransform)Get(mirror,"RulesContent")).gameObject.activeInHierarchy,"viewer-local hidden source cannot hide owner-authored peer rules");host.gameObject.SetActive(true);
        Rows(rules.Take(2).ToArray());yield return new WaitForSeconds(.3f);Call(fold,"Tick",.215f,.016f);Canvas.ForceUpdateCanvases();
        Call(mirror,"Refresh",content);Apply(Sample());Check((bool)Call(mirror,"Refresh",content),"native row removal rebuild earns a new valid owner fit");Check(Get(player,"_foldout")!=null,"remote inline presentation rebinds rebuilt clone");
        Rows(new[]{"Alle Gegner besiegen."});yield return new WaitForSeconds(.3f);Call(fold,"Tick",.215f,.016f);Canvas.ForceUpdateCanvases();
        Check(!(bool)Get(fold,"Overflow"),"short native rules remain complete without a replacement header");Call(mirror,"Refresh",content);yield return null;Apply(Sample());Render("short-owner-remote.png");PixelParity("short-rebuilt");
        Rows(new[]{string.Join("\n",Enumerable.Repeat(rules[2],14))});yield return new WaitForSeconds(.3f);Call(fold,"Tick",.215f,.016f);Call(fold,"Toggle");
        for(int i=0;i<7;i++){yield return null;Call(fold,"Tick",.215f,.04f);}Canvas.ForceUpdateCanvases();state=Sample();Apply(state);
        int veryLongBytes=EncodedBytes(state);float[] frame=(float[])Get(state,"Frame");
        Check(Mathf.Abs(frame[4]-frame[6])<.1f&&frame[7]==0f,"even extreme original prose opens in full without a replacement summary or scrolling cut");
        Check(content.GetChild(0).GetComponent<TMP_Text>().text.Contains(rules[2]),"complete original long rule remains intact");Render("long-full-inline-owner-remote.png");ClockProof(state);
        // Rules absence is a protocol mode edge: pristine original layout+TMP and host restored.
        Rows(new[]{"Alle Gegner besiegen."});yield return new WaitForSeconds(.3f);Call(fold,"Tick",.215f,.016f);Canvas.ForceUpdateCanvases();
        var legacy=Board(null,Time.unscaledTime);Call(player,"SetState",legacy,History(legacy),clock);Call(player,"Apply",mirror,remoteMount);
        Check(Get(player,"_foldout")==null&&Get(mirror,"RulesContent")==null,"absence96 destroys owner styling once rather than retaining altered legacy TMP");
        Check((bool)Call(mirror,"Refresh",content),"legacy original widget rebuild remains usable after expanded96");
        object fresh=NewMirror(layout);Call(fresh,"Refresh",content);
        Check(((RectTransform)Get(fresh,"RulesHost")).pivot==((RectTransform)Get(mirror,"RulesHost")).pivot&&((RectTransform)Get(fresh,"RulesHost")).sizeDelta==((RectTransform)Get(mirror,"RulesHost")).sizeDelta,"absence96 geometry matches fresh legacy mirror exactly");Call(fresh,"Destroy");
        // Actual sampler publishes one immutable object only on picture change, excludes private goal.
        state=Sample();Check(ReferenceEquals(state,Sample()),"unchanged original picture reuses immutable publication");
        File.WriteAllText(Path.Combine(output,"payload.json"),"{\"three_row_bytes\":"+threeRowBytes+",\"very_long_row_bytes\":"+veryLongBytes+",\"native_font_glyphs\":"+font.glyphTable.Count+"}");Check(threeRowBytes<12000&&veryLongBytes<12000,"full native long-text/style payload remains bounded and compressed");
        Rows(Array.Empty<string>());yield return new WaitForSeconds(.3f);Call(fold,"Tick",.215f,.016f);state=Sample();Apply(state);
        Check(!(bool)Get(state,"Visible")&&((float[])Get(state,"Frame"))[12]==0f,"empty rules publish explicit absence and reserve no invisible column");
        Check(Get(player,"_foldout")==null,"empty owner rules remove obsolete peer presentation");
        Call(ruleSurface,"Tick");
        Check(Get(Type("GloomhavenVR.WorldUI.Surfaces.ScenarioRulesSurface"),"Presentation")==null&&content.parent==home.transform,"map conversion gate releases original rules back to native home");
        Rows(rules);yield return null;
        panel=Call(Type("GloomhavenVR.WorldUI.CanvasConversion"),"Convert",content,"RulesProofReentry",true,null,false);host=(RectTransform)Get(panel,"HostRect");Set(ruleSurface,"Panel",panel);Call(ruleSurface,"OnConverted");
        fold=Get(Type("GloomhavenVR.WorldUI.Surfaces.ScenarioRulesSurface"),"Presentation");
        Call(fold,"Tick",.215f,.016f);Call(ruleSurface,"Place");Canvas.ForceUpdateCanvases();first=content.GetChild(0).GetComponent<TMP_Text>();first.ForceMeshUpdate();
        Check(!(bool)Get(fold,"Expanded")&&first.textInfo.characterInfo.Any(c=>c.isVisible),"scenario reentry restores the native visible preview without remembered hover or pinned state");
        Call(ruleSurface,"Shutdown");
        Check(content.parent==home.transform&&content.GetComponent<VerticalLayoutGroup>().enabled,"teardown restores original layout and native ownership");
        Call(Type("GloomhavenVR.WorldUI.CanvasConversion"),"Release",objectivePanel);Call(Type("GloomhavenVR.WorldUI.CanvasConversion"),"Release",elementPanel);
        Set(Type("GloomhavenVR.Cards.PlayTray"),"Current",previousTray);
        Call(mirror,"Destroy");Set(manager.GetType(),"Instance",previousManager);yield return null;
    }
    void BuildColumnProof(TMP_FontAsset font)
    {
        Type trayType=Type("GloomhavenVR.Cards.PlayTray");previousTray=Get(trayType,"Current");columnTray=System.Runtime.Serialization.FormatterServices.GetUninitializedObject(trayType);
        Set(columnTray,"_objectivesMount",mount);var elementMount=new GameObject("Native element seat").transform;elementMount.position=new Vector3(0,-.12f,0);Set(columnTray,"_elementMount",elementMount);Set(trayType,"Current",columnTray);
        Type config=Type("GloomhavenVR.Cards.CardsConfig"),boardType=Type("GloomhavenVR.Cards.ControlBoard");
        object configFile=Call(Type("GloomhavenVR.Core.ModuleConfig"),"Create","cards-column-proof");
        // Only these configuration inputs are read by the production column placement. Binding
        // the unrelated hand/vector settings would require the player's loader converters.
        var bind=configFile.GetType().GetMethods(All).Single(m=>m.Name=="Bind"&&m.IsGenericMethodDefinition&&m.GetParameters().Length==4&&m.GetParameters()[0].ParameterType==typeof(string)&&m.GetParameters()[3].ParameterType==typeof(string));
        Set(config,"Board",bind.MakeGenericMethod(boardType).Invoke(configFile,new object[]{"Proof","Board",Enum.GetValues(boardType).GetValue(0),"Native column input"}));
        int count=Enum.GetValues(boardType).Length;Type entry=Assembly.Load("BepInEx").GetType("BepInEx.Configuration.ConfigEntry`1").MakeGenericType(typeof(float));Array widths=Array.CreateInstance(entry,count);
        for(int i=0;i<count;i++)widths.SetValue(bind.MakeGenericMethod(typeof(float)).Invoke(configFile,new object[]{"Proof","Width"+i,.215f/.26f,"Native column input"}),i);Set(config,"_objectivesWidth",widths);
        objectiveSurface=New("GloomhavenVR.WorldUI.Surfaces.ObjectivesSurface");ruleSurface=New("GloomhavenVR.WorldUI.Surfaces.ScenarioRulesSurface");elementSurface=New("GloomhavenVR.WorldUI.Surfaces.ElementBoardSurface");
        var objective=(RectTransform)new GameObject("Column objective geometry",typeof(RectTransform),typeof(Image)).transform;objective.sizeDelta=new Vector2(.215f*1440f,80);
        objectivePanel=Call(Type("GloomhavenVR.WorldUI.CanvasConversion"),"Convert",objective,"RulesColumnObjectivesProof",true,null,false);RectTransform objectiveHost=(RectTransform)Get(objectivePanel,"HostRect");objectiveHost.sizeDelta=objective.sizeDelta;objectiveHost.pivot=new Vector2(.5f,.5f);objectiveHost.position=new Vector3(-.215f*.5f,0,0);objectiveHost.localScale=Vector3.one/1440f;
        Set(objectiveSurface,"Panel",objectivePanel);Set(Type("GloomhavenVR.WorldUI.Surfaces.ObjectivesSurface"),"DockedHeightMeters",80f/1440f);
        goal=new GameObject("Independent private goal seat",typeof(RectTransform),typeof(TextMeshProUGUI));goal.GetComponent<TMP_Text>().font=font;goal.GetComponent<TMP_Text>().text="PRIVATE-GOAL-MUST-STAY-LOCAL";Set(objectiveSurface,"_questGo",goal);
        var element=(RectTransform)new GameObject("Actual caption rectangle",typeof(RectTransform),typeof(Image)).transform;element.sizeDelta=new Vector2(.215f*1440f,70);
        elementPanel=Call(Type("GloomhavenVR.WorldUI.CanvasConversion"),"Convert",element,"RulesColumnElementsProof",true,null,false);Set(elementSurface,"Panel",elementPanel);Set(elementSurface,"_captionGraphics",element.GetComponentsInChildren<Graphic>(true));
        Set(ruleSurface,"Panel",panel);Set(panel,"FitEnabled",false);
    }
    void ColumnProof()
    {
        goal.SetActive(true);((GameObject)Get(objectivePanel,"HostGo")).SetActive(true);((GameObject)Get(elementPanel,"HostGo")).SetActive(true);
        Call(ruleSurface,"Place");Check((bool)Call(objectiveSurface,"PlaceQuestLabel"),"actual private-goal seat consumes the current original rule height");Call(elementSurface,"Place");
        var corners=new Vector3[4];host.GetWorldCorners(corners);float rulesBottom=mount.InverseTransformPoint(corners[0]).y;
        float goalTop=mount.InverseTransformPoint(goal.transform.position).y+.215f*.34f*.5f;
        float goalBottom=mount.InverseTransformPoint(goal.transform.position).y-.215f*.34f*.5f;
        Check(goalTop<=rulesBottom-.002f,"actual native goal stays below the enlarged or collapsed rule viewport");
        RectTransform elementHost=(RectTransform)Get(elementPanel,"HostRect");elementHost.GetWorldCorners(corners);float elementTop=corners.Max(c=>mount.InverseTransformPoint(c).y);
        Check(elementTop<=goalBottom-.005f,"actual element caption union clears the native goal on every intermediate frame");
        float drop=(float)Get(Type("GloomhavenVR.WorldUI.Surfaces.ScenarioRulesSurface"),"DockedDropMeters");
        Check(Mathf.Abs(drop-.012f-(float)Get(fold,"OccupiedMeters"))<.00001f,"actual surface publishes precisely the animated rule occupancy plus its normal gap");
        ((GameObject)Get(objectivePanel,"HostGo")).SetActive(false);((GameObject)Get(elementPanel,"HostGo")).SetActive(false);goal.SetActive(false);
    }
    void ClickRules(Button button,PointerEventData data)
    {
        Call(vrPointer,"SetHovered",button.gameObject);Call(vrPointer,"Press",data.position);Call(vrPointer,"Release",data.position);Call(vrPointer,"SetHovered",null,true);
    }
    bool HasVisibleEllipsis(TMP_Text text)
    {
        text.ForceMeshUpdate();string painted=string.Concat(text.textInfo.characterInfo.Take(text.textInfo.characterCount).Where(c=>c.isVisible).Select(c=>c.character));
        return painted.EndsWith("...")||painted.EndsWith("\u2026");
    }
    object NewMirror(Type layout)=>Activator.CreateInstance(Type("GloomhavenVR.Net.RemoteWidgetMirror"),All,null,new object[]{"RulesProofMirror",remoteMount,.215f,.09f,new Vector2(-1,-1),false,.6f,false,Enum.Parse(layout,"CloneAtBoardOwnersWidth"),null,false,null,null,false},null);
    void Rows(string[] text){foreach(Transform row in content.Cast<Transform>().ToArray())Object.DestroyImmediate(row.gameObject);foreach(string value in text){GameObject row=Object.Instantiate(prefab,content,false);row.name="Scenario modifier";row.GetComponent<TMP_Text>().text=value;}}
    object Sample()=>Call(Type("GloomhavenVR.WorldUI.Surfaces.BoardRulesPresentation"),"Sample");
    int EncodedBytes(object rules)=> (int)Call(Type("GloomhavenVR.Net.NativeBoardCodec"),"Write",Board(rules,Time.unscaledTime),new byte[65535]);
    Rect Ink(object converted){object[] args={converted,null,true,null,null,null,null,true,null,null};var method=Type("GloomhavenVR.WorldUI.PanelInkBounds").GetMethod("TryMeasure",All);Check((bool)method.Invoke(null,args),"production visible painted bounds measure actual native hierarchy");return (Rect)Get(args[1],"Rect");}
    void ClockProof(object rules)
    {
        object shared=New("GloomhavenVR.Net.NativeBoardPresentationClock");object first=Board(rules,1f),second=Board(rules,1.05f),last=Board(rules,1.10f);var history=(IList)History(first);history.Add(second);history.Add(last);
        var select=shared.GetType().GetMethod("Select",All);object[] a={last,history,null,null,0f};select.Invoke(shared,a);object[] b={last,history,null,null,0f};select.Invoke(shared,b);
        Check(ReferenceEquals(a[2],first)&&ReferenceEquals(a[2],b[2])&&ReferenceEquals(a[3],b[3])&&(float)a[4]==(float)b[4],"shared clock starts oldest buffered owner frame and latches identical rules/element pair");
        object changed=BoardGeneration(rules,2f,1u);object[] c={changed,History(changed),null,null,0f};select.Invoke(shared,c);Check(ReferenceEquals(c[2],changed),"generation reset replaces same-frame clock history atomically");
    }
    object BoardGeneration(object rules,float time,uint generation)
    {
        Type element=Type("GloomhavenVR.Net.NativeElementState"),graphic=Type("GloomhavenVR.Net.NativeElementGraphic"),value=Type("GloomhavenVR.Net.UseBarAnimationValue");Array elements=Array.CreateInstance(element,6);
        for(int i=0;i<6;i++){object e=Activator.CreateInstance(element,true);Set(e,"Sibling",(byte)i);Array graphics=Array.CreateInstance(graphic,7);for(int j=0;j<7;j++)graphics.SetValue(Activator.CreateInstance(graphic,true),j);Set(e,"Graphics",graphics);Array anim=Array.CreateInstance(value.MakeArrayType(),3);for(int j=0;j<3;j++)anim.SetValue(Array.CreateInstance(value,0),j);Set(e,"Animations",anim);elements.SetValue(e,i);}
        return New("GloomhavenVR.Net.NativeBoardState",time,15f,generation,elements,null,null,rules);
    }
    object Board(object rules,float time){var elements=Array.CreateInstance(Type("GloomhavenVR.Net.NativeElementState"),0);return New("GloomhavenVR.Net.NativeBoardState",time,15f,(uint)0,elements,null,null,rules);}
    object History(object board){var list=Activator.CreateInstance(typeof(List<>).MakeGenericType(Type("GloomhavenVR.Net.NativeBoardState")));((IList)list).Add(board);return list;}
    void Apply(object rules){var board=WireBoard(rules);Call(player,"SetState",board,History(board),clock);Call(player,"Apply",mirror,remoteMount);Call(mirror,"SetShown",true);Canvas.ForceUpdateCanvases();}
    object WireBoard(object rules)
    {
        var buffer=new byte[65535];int count=(int)Call(Type("GloomhavenVR.Net.NativeBoardCodec"),"Write",Board(rules,Time.unscaledTime),buffer);
        object[] decoded={buffer,count,null};Check((bool)Type("GloomhavenVR.Net.NativeBoardCodec").GetMethod("TryRead",All).Invoke(null,decoded),"actual additive rules codec decodes owner snapshot before remote playback");
        foreach(object row in (IEnumerable)Get(Get(decoded[2],"Rules"),"Rows"))foreach(object text in (IEnumerable)Get(row,"Text"))Check(!((string)Get(text,"Text")).Contains("PRIVATE-GOAL-MUST-STAY-LOCAL"),"private goal never enters public rules presentation");
        return decoded[2];
    }
    void Render(string name){Canvas.ForceUpdateCanvases();RenderTexture rt=new RenderTexture(1800,1000,24);camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;Texture2D image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();File.WriteAllBytes(Path.Combine(output,name),image.EncodeToPNG());camera.targetTexture=null;RenderTexture.active=null;Object.Destroy(rt);Object.Destroy(image);}
    void PixelParity(string name)
    {
        RectTransform peer=(RectTransform)Get(mirror,"RulesHost");Vector3 saved=camera.transform.position;
        peer.gameObject.SetActive(false);camera.transform.position=new Vector3(-.3f,-.11f,-2f);Color32[] owner=Pixels();
        // Isolate rendered ink without disabling owner gameplay/input components: disabling the
        // host itself invokes the real pointer OnDisable exit and correctly ends a temporary hover.
        Canvas ownerCanvas=host.GetComponent<Canvas>();bool enabled=ownerCanvas.enabled;ownerCanvas.enabled=false;peer.gameObject.SetActive(true);camera.transform.position+=peer.position-host.position;Color32[] remote=Pixels();
        ownerCanvas.enabled=enabled;camera.transform.position=saved;int painted=0,changed=0;Color32 bg=owner[0];
        for(int i=0;i<owner.Length;i++){if(Distance(owner[i],bg)>6)painted++;if(Distance(owner[i],remote[i])>6)changed++;}
        Check(painted>200,"actual original owner glyph/header pixels are visible: "+name);
        Check(changed<Mathf.Max(30,painted*.015f),"separate actual owner/peer native rendered pixel parity: "+name+" ("+changed+"/"+painted+")");
    }
    static int Distance(Color32 a,Color32 b)=>Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b);
    Color32[] Pixels(){Canvas.ForceUpdateCanvases();RenderTexture rt=new RenderTexture(1200,720,24);camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;Texture2D image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();Color32[] result=image.GetPixels32();camera.targetTexture=null;RenderTexture.active=null;Object.Destroy(rt);Object.Destroy(image);return result;}
    void Finish(bool passed,string error){string result="{\"passed\":"+passed.ToString().ToLowerInvariant()+",\"assertions\":"+checks.Count+",\"controls\":"+controls+",\"error\":\""+(error??"").Replace("\\","\\\\").Replace("\"","\\\"").Replace("\n","\\n").Replace("\r","")+"\"}";File.WriteAllText(Path.Combine(output,"results.json"),result);File.WriteAllLines(Path.Combine(output,"assertions.txt"),checks);EditorApplication.Exit(passed?0:1);}
    static string Argument(string name){var args=Environment.GetCommandLineArgs();for(int i=0;i<args.Length-1;i++)if(args[i]==name)return args[i+1];throw new Exception("missing "+name);}
}
