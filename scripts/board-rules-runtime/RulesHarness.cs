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
    Assembly mod,game; object fold,panel,mirror,player,clock; RectTransform content,host,mount,remoteMount;
    GameObject nativeManager; GameObject prefab; Camera camera; object previousManager;
    Type Type(string name)=>mod.GetType(name,true);
    object Get(object obj,string name){var type=obj as Type??obj.GetType();for(;type!=null;type=type.BaseType){var f=type.GetField(name,All);if(f!=null)return f.GetValue(obj is Type?null:obj);var p=type.GetProperty(name,All);if(p!=null)return p.GetValue(obj is Type?null:obj);}throw new Exception("Missing field "+name);}
    void Set(object obj,string name,object value){var type=obj as Type??obj.GetType();for(;type!=null;type=type.BaseType){var f=type.GetField(name,All);if(f!=null){f.SetValue(obj is Type?null:obj,value);return;}var p=type.GetProperty(name,All);if(p!=null){p.SetValue(obj is Type?null:obj,value);return;}}throw new Exception("Missing field "+name);}
    object Call(object obj,string name,params object[] args){var type=obj as Type??obj.GetType();var methods=type.GetMethods(All).Where(m=>m.Name==name&&m.GetParameters().Length>=args.Length);foreach(var method in methods){var p=method.GetParameters();if(p.Take(args.Length).Where((x,i)=>args[i]!=null&&!x.ParameterType.IsInstanceOfType(args[i])&&!x.ParameterType.IsByRef).Any())continue;if(p.Skip(args.Length).Any(x=>!x.IsOptional))continue;var values=new object[p.Length];Array.Copy(args,values,args.Length);for(int i=args.Length;i<p.Length;i++)values[i]=p[i].DefaultValue;try{return method.Invoke(obj is Type?null:obj,values);}catch(TargetInvocationException e){throw new Exception("Production invocation: "+type+"."+name,e.InnerException);}}throw new Exception("Missing method "+type+"."+name);}
    object New(string name,params object[] args)=>Activator.CreateInstance(Type(name),All,null,args,null);
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
        prefab=Resources.Load<GameObject>("NativeRule");Check(prefab!=null,"original serialized native rule prefab loaded");
        var font=Resources.Load<TMP_FontAsset>("NativeRulesFont");Check(font.characterTable.Count>700&&font.atlasTextures[0].width==2048,"native font glyph tables and lossless original atlas recovered");
        Set(Type("GloomhavenVR.WorldUI.NativeButtonSkin"),"_sampled",true);
        Set(Type("GloomhavenVR.WorldUI.NativeButtonSkin"),"_font",font);
        Set(Type("GloomhavenVR.WorldUI.NativeButtonSkin"),"_normalSprite",Resources.Load<Sprite>("NativeButton"));
        camera=new GameObject("Evidence camera").AddComponent<Camera>();camera.gameObject.tag="MainCamera";camera.orthographic=true;camera.orthographicSize=.65f;camera.backgroundColor=new Color(.07f,.075f,.085f);camera.clearFlags=CameraClearFlags.SolidColor;camera.transform.position=new Vector3(.15f,-.11f,-2f);
        new GameObject("Events",typeof(EventSystem),typeof(StandaloneInputModule));
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
        Check((bool)Get(fold,"Overflow")&&!(bool)Get(fold,"Expanded"),"three original screenshot rules collapse to localized header at readable density");
        Check(content.gameObject.activeInHierarchy,"collapsed original native widget stays alive; no lifecycle continuation suppression");
        Vector3[] corners=new Vector3[4];((RectTransform)host.Find("GloomhavenVR.RulesFoldout/SpecialRules")).GetWorldCorners(corners);
        Check(host.InverseTransformPoint(corners[2]).magnitude<.01f,"header top-right origin exactly follows host; no default100px wrapper displacement");
        var header=host.Find("GloomhavenVR.RulesFoldout/SpecialRules").GetComponent<Button>();
        Check(header.transition==Selectable.Transition.None,"native header tint has no viewer-local Selectable transition");
        var eventData=new PointerEventData(EventSystem.current);var point=RectTransformUtility.WorldToScreenPoint(camera,header.transform.position);eventData.position=point;
        Get(panel,"HostCanvas");host.GetComponent<Canvas>().worldCamera=camera;
        var hits=new List<RaycastResult>();((GraphicRaycaster)Get(panel,"HostRaycaster")).Raycast(eventData,hits);
        Check(hits.Any(h=>h.gameObject==header.gameObject),"real production registered host GraphicRaycaster hits foldout header");
        object closed=Sample();int threeRowBytes=EncodedBytes(closed);
        ExecuteEvents.Execute(header.gameObject,eventData,ExecuteEvents.pointerEnterHandler);ExecuteEvents.Execute(header.gameObject,eventData,ExecuteEvents.pointerClickHandler);
        Check((bool)Get(fold,"Expanded"),"real native pointer click opens presentation-only foldout");
        object state=Sample();float[] frame=(float[])Get(state,"Frame");Check((bool)Get(state,"Hover"),"actual owner pointer hover captured");
        var remoteBoard=new GameObject("Actual remote board root").transform;remoteBoard.position=new Vector3(1f,0,0);
        remoteMount=(RectTransform)new GameObject("Remote objective mount",typeof(RectTransform)).transform;remoteMount.SetParent(remoteBoard,false);
        var layout=Type("GloomhavenVR.Net.RemoteWidgetMirror+LayoutOwner");mirror=NewMirror(layout);
        Call(mirror,"Refresh",content);Check(Get(mirror,"RulesContent")!=null,"original native widget cloned through production neutralization even before owner geometry");player=New("GloomhavenVR.Net.RemoteBoardRulesPlayer");clock=New("GloomhavenVR.Net.NativeBoardPresentationClock");
        Apply(closed);Check((bool)Call(mirror,"Refresh",content),"first collapsed owner frame establishes valid fit without viewer-visible native text");
        Check(!(bool)Get(Get(player,"_foldout"),"Expanded"),"initial closed owner remains closed on peer");Render("long-collapsed-owner-remote.png");
        Check(Vector3.Distance(host.position,((RectTransform)Get(mirror,"RulesHost")).position)>.9f,"actual separate board parent poses preserve visible owner and peer placement");
        Set(panel,"MrVisualRoot",Get(fold,"VisualRoot"));Rect collapsedInk=Ink(panel);
        Check(collapsedInk.height<60f,"actual MR ink includes collapsed header and excludes alpha-zero original prose");
        for(int i=0;i<4;i++)
        {
            Call(fold,"Tick",.215f,.04f);yield return null;Canvas.ForceUpdateCanvases();state=Sample();Apply(state);
            var remoteFold=Get(player,"_foldout");Check(remoteFold!=null,"remote original native foldout binds successfully");
            object remoteState=New("GloomhavenVR.Net.NativeBoardRulesState");Call(remoteFold,"CaptureFrame",remoteState);
            float[] a=(float[])Get(state,"Frame"),b=(float[])Get(remoteState,"Frame");Check(Mathf.Abs(a[3]-b[3])<.001f,"owner-authored intermediate animation geometry identical on remote frame "+i);
            var remoteText=((RectTransform)Get(mirror,"RulesContent")).GetComponentsInChildren<TMP_Text>(true);
            Check(remoteText.Length==3&&remoteText[0].text==rules[0]&&remoteText[0].font==font&&remoteText[0].fontSize==16,"remote uses complete original text/font/native16px size, not rebuilt summaries");
            Render("long-intermediate-"+i+"-owner-remote.png");
            PixelParity("intermediate-"+i);
        }
        Check((float)Get(fold,"OccupiedMeters")<.04f,"expanded wide panel leaves only header in reserved battle-goal column");
        var viewport=(RectTransform)host.Find("GloomhavenVR.RulesFoldout/Viewport");viewport.GetWorldCorners(corners);
        Check(Mathf.Abs(host.InverseTransformPoint(corners[2]).y)<.01f,"expanded viewport top matches header top without vertical shift");
        Check(frame[10]==1f/1440f,"native text keeps fixed1440pixel/m density instead of0.5 shrink");
        Rect openInk=Ink(panel);Check(openInk.width>collapsedInk.width&&openInk.height<=.28f*1440f+2f,"actual opened MR ink includes foldout clipped to bounded viewport");
        var stylePanel=System.Runtime.Serialization.FormatterServices.GetUninitializedObject(Type("GloomhavenVR.Net.RemoteObjectivesPanel"));
        Set(stylePanel,"_root",remoteMount);Set(stylePanel,"_rulesRoot",remoteMount);Set(stylePanel,"_nativeRules",player);
        TMP_Text ownerHeader=header.GetComponentInChildren<TMP_Text>(),peerHeader=((RectTransform)Get(mirror,"RulesHost")).Find("GloomhavenVR.RulesFoldout/SpecialRules").GetComponentInChildren<TMP_Text>();
        Call(stylePanel,"StyleMirroredText");Canvas.ForceUpdateCanvases();
        Check(ownerHeader.fontSharedMaterial.GetFloat("_OutlineWidth")==peerHeader.fontSharedMaterial.GetFloat("_OutlineWidth")&&ownerHeader.fontSharedMaterial.IsKeywordEnabled("UNDERLAY_ON")==peerHeader.fontSharedMaterial.IsKeywordEnabled("UNDERLAY_ON"),"remote objectives style sweep preserves actual original header SDF material");
        // Deliberately trigger a receiver layout rebuild after owner application. Its native stock
        // padding and preferred heights must not overwrite the owner's sampled geometry.
        RectTransform remoteContent=(RectTransform)Get(mirror,"RulesContent");var group=remoteContent.GetComponent<VerticalLayoutGroup>();
        group.padding=new RectOffset(900,900,900,900);Canvas.ForceUpdateCanvases();
        Check(!group.enabled&&remoteContent.GetChild(0).GetComponent<RectTransform>().sizeDelta==content.GetChild(0).GetComponent<RectTransform>().sizeDelta,"disabled cloned layout cannot rewrite owner row geometry after canvas rebuild");
        group.enabled=true;LayoutRebuilder.ForceRebuildLayoutImmediate(remoteContent);
        Control(remoteContent.GetChild(0).GetComponent<RectTransform>().sizeDelta!=content.GetChild(0).GetComponent<RectTransform>().sizeDelta,"re-enabled competing native layout breaks row geometry");group.enabled=false;Apply(Sample());
        host.gameObject.SetActive(false);Call(mirror,"Refresh",content);Apply(state);
        Check(((RectTransform)Get(mirror,"RulesContent")).gameObject.activeInHierarchy,"viewer local original host hidden cannot hide owner-authored peer rules");host.gameObject.SetActive(true);
        // Dynamic native row change exercises clone lifetime, initial fit and array binding edges.
        Rows(rules.Take(2).ToArray());yield return new WaitForSeconds(.3f);Call(fold,"Tick",.215f,.016f);Canvas.ForceUpdateCanvases();
        Call(mirror,"Refresh",content);Apply(Sample());Check((bool)Call(mirror,"Refresh",content),"native row removal rebuild earns new valid owner fit");Check(Get(player,"_foldout")!=null,"remote foldout rebinds rebuilt clone");
        Rows(new[]{"Alle Gegner besiegen."});yield return new WaitForSeconds(.3f);Call(fold,"Tick",.215f,.016f);Canvas.ForceUpdateCanvases();
        Check(!(bool)Get(fold,"Overflow"),"short native rules stay visible with no needless header");Call(mirror,"Refresh",content);yield return null;Apply(Sample());Render("short-owner-remote.png");PixelParity("short-rebuilt");
        // Very long original native text requires viewport scrolling, never text truncation.
        Rows(new[]{string.Join("\n",Enumerable.Repeat(rules[2],14))});yield return new WaitForSeconds(.3f);Call(fold,"Tick",.215f,.016f);Call(fold,"Toggle");
        for(int i=0;i<5;i++){yield return null;Call(fold,"Tick",.215f,.04f);}Canvas.ForceUpdateCanvases();
        var scroll=host.Find("GloomhavenVR.RulesFoldout").GetComponent<ScrollRect>();scroll.verticalNormalizedPosition=.25f;Canvas.ForceUpdateCanvases();state=Sample();yield return null;Apply(state);
        int veryLongBytes=EncodedBytes(state);Check(((float[])Get(state,"Frame"))[7]>0,"original long text scroll position authored by owner");
        var peer=Get(player,"_foldout");var scrollState=New("GloomhavenVR.Net.NativeBoardRulesState");Call(peer,"CaptureFrame",scrollState);
        Check(Mathf.Abs(((float[])Get(scrollState,"Frame"))[7]-((float[])Get(state,"Frame"))[7])<.01f,"remote applies exact owner scroll without own clock or input");Render("long-scroll-owner-remote.png");
        Check(viewport.rect.height<=.28f*1440f+.1f,"extreme native content clips to bounded280mm viewport");
        Check(Ink(panel).height<=.28f*1440f+2f,"actual scrolled MR union cannot grow to unbounded native text height");
        ClockProof(state);
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
        var restoreParent=(RectTransform)Get(fold,"_originalParent");Call(fold,"Dispose");Call(Type("GloomhavenVR.WorldUI.CanvasConversion"),"Release",panel);
        Check(content.parent==home.transform,"teardown restores native container ownership, never destroyed with board");
        Call(mirror,"Destroy");Set(manager.GetType(),"Instance",previousManager);yield return null;
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
    void Apply(object rules){var board=Board(rules,Time.unscaledTime);Call(player,"SetState",board,History(board),clock);Call(player,"Apply",mirror,remoteMount);Call(mirror,"SetShown",true);Canvas.ForceUpdateCanvases();}
    void Render(string name){Canvas.ForceUpdateCanvases();RenderTexture rt=new RenderTexture(1800,1000,24);camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;Texture2D image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();File.WriteAllBytes(Path.Combine(output,name),image.EncodeToPNG());camera.targetTexture=null;RenderTexture.active=null;Object.Destroy(rt);Object.Destroy(image);}
    void PixelParity(string name)
    {
        RectTransform peer=(RectTransform)Get(mirror,"RulesHost");Vector3 saved=camera.transform.position;
        peer.gameObject.SetActive(false);camera.transform.position=new Vector3(-.3f,-.11f,-2f);Color32[] owner=Pixels();
        host.gameObject.SetActive(false);peer.gameObject.SetActive(true);camera.transform.position+=peer.position-host.position;Color32[] remote=Pixels();
        host.gameObject.SetActive(true);camera.transform.position=saved;int painted=0,changed=0;Color32 bg=owner[0];
        for(int i=0;i<owner.Length;i++){if(Distance(owner[i],bg)>6)painted++;if(Distance(owner[i],remote[i])>6)changed++;}
        Check(painted>200,"actual original owner glyph/header pixels are visible: "+name);
        Check(changed<Mathf.Max(30,painted*.015f),"separate actual owner/peer native rendered pixel parity: "+name+" ("+changed+"/"+painted+")");
    }
    static int Distance(Color32 a,Color32 b)=>Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b);
    Color32[] Pixels(){Canvas.ForceUpdateCanvases();RenderTexture rt=new RenderTexture(1200,720,24);camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;Texture2D image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();Color32[] result=image.GetPixels32();camera.targetTexture=null;RenderTexture.active=null;Object.Destroy(rt);Object.Destroy(image);return result;}
    void Finish(bool passed,string error){string result="{\"passed\":"+passed.ToString().ToLowerInvariant()+",\"assertions\":"+checks.Count+",\"controls\":"+controls+",\"error\":\""+(error??"").Replace("\\","\\\\").Replace("\"","\\\"").Replace("\n","\\n").Replace("\r","")+"\"}";File.WriteAllText(Path.Combine(output,"results.json"),result);File.WriteAllLines(Path.Combine(output,"assertions.txt"),checks);EditorApplication.Exit(passed?0:1);}
    static string Argument(string name){var args=Environment.GetCommandLineArgs();for(int i=0;i<args.Length-1;i++)if(args[i]==name)return args[i+1];throw new Exception("missing "+name);}
}
