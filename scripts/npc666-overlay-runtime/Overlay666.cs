using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ScenarioRuleLibrary;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

public static partial class MirrorProgram
{
    private static Vector2 V2665(BinaryReader r) => new(r.ReadSingle(),r.ReadSingle());
    private static Vector3 V3665(BinaryReader r) => new(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());
    private static Color C665(BinaryReader r) => new(r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),r.ReadSingle());
    private static string S665(BinaryReader r) => System.Text.Encoding.UTF8.GetString(r.ReadBytes(r.ReadUInt16()));
    internal static GameObject Import666(string key,Transform parent)
    {
        using var r=new BinaryReader(File.OpenRead(Native666.Data+"/"+key+".bin"));
        int count=r.ReadUInt16();var nodes=new RectTransform[count];int[]? references=null;SummonContainer? summon=null;
        for(int i=0;i<count;i++)
        {
            string name=S665(r);int p=r.ReadInt16();bool active=r.ReadBoolean();
            var node=(RectTransform)Go(name,p<0?parent:nodes[p]).transform;nodes[i]=node;
            node.localPosition=V3665(r);node.localRotation=new Quaternion(r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),r.ReadSingle());
            node.localScale=V3665(r);node.anchorMin=V2665(r);node.anchorMax=V2665(r);node.anchoredPosition=V2665(r);node.sizeDelta=V2665(r);node.pivot=V2665(r);
            int components=r.ReadByte();
            for(int c=0;c<components;c++)
            {
                int type=r.ReadByte();bool enabled=r.ReadBoolean();
                if(type==1||type==2)
                {
                    HorizontalOrVerticalLayoutGroup group=type==1?node.gameObject.AddComponent<HorizontalLayoutGroup>():node.gameObject.AddComponent<VerticalLayoutGroup>();
                    group.padding=new RectOffset(r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32());group.childAlignment=(TextAnchor)r.ReadInt32();group.spacing=r.ReadSingle();
                    group.childForceExpandWidth=r.ReadBoolean();group.childForceExpandHeight=r.ReadBoolean();group.childControlWidth=r.ReadBoolean();group.childControlHeight=r.ReadBoolean();group.childScaleWidth=r.ReadBoolean();group.childScaleHeight=r.ReadBoolean();group.enabled=enabled;
                }
                else if(type==3)
                { var fitter=node.gameObject.AddComponent<ContentSizeFitter>();fitter.horizontalFit=(ContentSizeFitter.FitMode)r.ReadInt32();fitter.verticalFit=(ContentSizeFitter.FitMode)r.ReadInt32();fitter.enabled=enabled; }
                else if(type==4)
                { var layout=node.gameObject.AddComponent<LayoutElement>();layout.minWidth=r.ReadSingle();layout.minHeight=r.ReadSingle();layout.preferredWidth=r.ReadSingle();layout.preferredHeight=r.ReadSingle();layout.flexibleWidth=r.ReadSingle();layout.flexibleHeight=r.ReadSingle();layout.layoutPriority=r.ReadInt32();layout.ignoreLayout=r.ReadBoolean();layout.enabled=enabled; }
                else if(type==5)
                { var text=node.gameObject.AddComponent<TextMeshProUGUI>();text.font=TMP_Settings.defaultFontAsset;text.text=S665(r);text.fontSize=r.ReadSingle();text.fontStyle=(FontStyles)r.ReadInt32();text.alignment=(TextAlignmentOptions)r.ReadInt32();text.color=C665(r);text.margin=new Vector4(r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),r.ReadSingle());text.enabled=enabled; }
                else if(type==6)
                { var image=node.gameObject.AddComponent<Image>();image.color=C665(r);image.raycastTarget=r.ReadBoolean();image.enabled=enabled; }
                else if(type==7)
                { summon=node.gameObject.AddComponent<SummonContainer>();references=new int[6];for(int j=0;j<6;j++)references[j]=r.ReadInt16();summon.enabled=enabled; }
                else if(type==8) { var group=node.gameObject.AddComponent<CanvasGroup>();group.alpha=r.ReadSingle();group.interactable=r.ReadBoolean();group.blocksRaycasts=r.ReadBoolean();group.ignoreParentGroups=r.ReadBoolean(); }
                else if(type==9) node.gameObject.AddComponent<EnhancementButton>();
                else throw new InvalidDataException("Unknown serialized layout component");
            }
            node.gameObject.SetActive(active);
        }
        Check(r.BaseStream.Position==r.BaseStream.Length,"native summon importer consumes exact serialized hierarchy");
        if(summon!=null) { summon.SummonNameText=nodes[references![0]].GetComponent<TMP_Text>();
        summon.SummonLT=nodes[references[1]].gameObject;summon.SummonLB=nodes[references[2]].gameObject;
        summon.SummonMT=nodes[references[3]].gameObject;summon.SummonMB=nodes[references[4]].gameObject;summon.SummonR=nodes[references[5]].gameObject;
        }
        return nodes[0].gameObject;
    }
    private static RectTransform[] Targets665(SummonContainer summon) => new[] { (RectTransform)summon.SummonLT.transform,(RectTransform)summon.SummonLB.transform,(RectTransform)summon.SummonMT.transform,(RectTransform)summon.SummonMB.transform };
    private static void Rebuild665(SummonContainer summon)
    { Canvas.ForceUpdateCanvases();LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)summon.transform);Canvas.ForceUpdateCanvases(); }
    private static void TargetCorners665(RectTransform expected,RectTransform actual,string invariant)
    {
        var a=new Vector3[4];var b=new Vector3[4];expected.GetWorldCorners(a);actual.GetWorldCorners(b);
        RectTransform print=(RectTransform)expected.GetComponentInParent<Canvas>().transform;
        Vector3 bias=-print.forward*(print.rect.height*print.TransformVector(Vector3.up).magnitude*.0003f);
        for(int i=0;i<4;i++)Check(Vector3.Distance(a[i]+bias,b[i])<.000015f,invariant);
    }
    private static int LabelPixels665(Graphic text,int layer,string name)
    {
        // uGUI can batch sibling graphics in one canvas despite per-leaf camera
        // layers. Isolate this actual source/observer label reversibly; keep its
        // native enabled, graphic/renderer colour, mesh and layout unchanged.
        var others=new List<Graphic>();var enabled=new List<bool>();
        foreach(Graphic g in text.canvas.GetComponentsInChildren<Graphic>(true))
        { if(g==text||g.transform.IsChildOf(text.transform))continue;others.Add(g);enabled.Add(g.enabled);g.enabled=false; }
        Transform[] canvasNodes=text.canvas.GetComponentsInChildren<Transform>(true);
        var layers=new int[canvasNodes.Length];for(int i=0;i<canvasNodes.Length;i++){layers[i]=canvasNodes[i].gameObject.layer;canvasNodes[i].gameObject.layer=layer;}
        Canvas.ForceUpdateCanvases();
        RectTransform rect=text.rectTransform;
        float width=rect.rect.width*rect.TransformVector(Vector3.right).magnitude;
        float height=rect.rect.height*rect.TransformVector(Vector3.up).magnitude;
        var target=new RenderTexture(256,96,24,RenderTextureFormat.ARGB32);target.Create();
        _camera.cullingMask=1<<layer;_camera.targetTexture=target;
        _camera.orthographicSize=Mathf.Max(height*.5f,width*.5f/(256f/96f))*1.1f;
        _camera.transform.SetPositionAndRotation(rect.TransformPoint(rect.rect.center)-rect.forward*.5f,Quaternion.LookRotation(rect.forward,rect.up));
        _camera.Render();RenderTexture previous=RenderTexture.active;RenderTexture.active=target;
        var image=new Texture2D(256,96,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,256,96),0,0);image.Apply();
        Color32[] pixels=image.GetPixels32();int ink=0;for(int i=0;i<pixels.Length;i++)if(!pixels[i].Equals(pixels[0]))ink++;
        File.WriteAllBytes(Path.Combine(_output,name+".png"),image.EncodeToPNG());
        RenderTexture.active=previous;_camera.targetTexture=null;target.Release();Object.Destroy(target);Object.Destroy(image);
        for(int i=0;i<others.Count;i++)if(others[i]!=null)others[i].enabled=enabled[i];
        for(int i=0;i<canvasNodes.Length;i++)if(canvasNodes[i]!=null)canvasNodes[i].gameObject.layer=layers[i];
        return ink;
    }
    private static bool Draws665(Graphic g) => g.enabled&&g.gameObject.activeInHierarchy&&!g.canvasRenderer.cull&&g.color.a*g.canvasRenderer.GetColor().a>0;
    private static void Set666(object target,string field,object value)
        => target.GetType().GetField(field,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(target,value);
    private static RectTransform Rect666(BinaryReader r,GameObject go)
    {
        var node=(RectTransform)go.transform;
        node.localPosition=V3665(r);node.localRotation=new Quaternion(r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),r.ReadSingle());node.localScale=V3665(r);
        node.anchorMin=V2665(r);node.anchorMax=V2665(r);node.anchoredPosition=V2665(r);node.sizeDelta=V2665(r);node.pivot=V2665(r);
        var image=go.AddComponent<Image>();image.color=C665(r);image.raycastTarget=r.ReadBoolean();
        image.canvasRenderer.cullTransparentMesh=r.ReadBoolean();
        if(r.ReadBoolean()) { var group=go.AddComponent<CanvasGroup>();group.alpha=r.ReadSingle();group.interactable=r.ReadBoolean();group.blocksRaycasts=r.ReadBoolean();group.ignoreParentGroups=r.ReadBoolean(); }
        return node;
    }
    private static UIEnhancementButtonHighlight HighlightPrefab666(Transform parent)
    {
        using var r=new BinaryReader(File.OpenRead(Native666.Data+"/highlight.bin"));
        var root=Go("Enhancement Ability Highlight Variant",parent);root.SetActive(false);Rect666(r,root);
        var fill=Go("Image",root.transform);Rect666(r,fill);
        var frame=Go("Frame",root.transform);Rect666(r,frame);
        // The serialized native original has ExtendedButton as a component;
        // loading a fixture DLL does not import its MonoScript asset metadata.
        root.AddComponent<ExtendedButton>();
        var native=root.AddComponent<UIEnhancementButtonHighlight>();
        Image ink=frame.GetComponent<Image>();ink.material=new Material(Shader.Find("GVR/RectFormat666"));
        Set666(native,"frame",ink);Set666(native,"fillImage",fill.GetComponent<CanvasGroup>());
        Set666(native,"opacitySelected",r.ReadSingle());Set666(native,"opacityHovered",r.ReadSingle());
        Set666(native,"invalidFrameColor",C665(r));Set666(native,"validFrameColor",C665(r));Set666(native,"shaderProperty",S665(r));
        Check(r.BaseStream.Position==r.BaseStream.Length,"native selectable import consumes original anchors pivot frame and hover settings exactly");
        Check(ink.material.GetVector("_RectFormat")==Vector4.zero,"native frame starts with an uninitialised rectangle format");
        return native;
    }
    private static CardLayoutGroup.SummonLayout? FindSummon666(CardLayoutGroup? group)
    {
        if(group==null)return null;if(group.Summon!=null)return group.Summon;
        if(group.Collection!=null)foreach(var child in group.Collection){var found=FindSummon666(child);if(found!=null)return found;}
        return null;
    }
    private static UIEnhancementButtonHighlight? Area666(RectTransform target)
        => target.GetComponentsInChildren<UIEnhancementButtonHighlight>(true).FirstOrDefault(a=>a.transform.parent==target&&a.gameObject.activeSelf);
    private static bool Hit666(Canvas canvas,RectTransform target,out RaycastResult hit)
    {
        canvas.worldCamera=_camera;var caster=canvas.GetComponent<GraphicRaycaster>()??canvas.gameObject.AddComponent<GraphicRaycaster>();
        _camera.cullingMask=~0;_camera.orthographicSize=.3f;
        _camera.transform.SetPositionAndRotation(target.position-target.forward*.7f,Quaternion.LookRotation(target.forward,target.up));
        Canvas.ForceUpdateCanvases();
        // The game has a rendering head camera. This headless fixture's camera
        // is disabled, so submit the real UI batch before asking for its depth.
        _camera.Render();
        var pointer=new PointerEventData(EventSystem.current){pointerId=-111,position=_camera.WorldToScreenPoint(target.TransformPoint(target.rect.center)),button=PointerEventData.InputButton.Left};
        bool found=new Pointer666().Pick(caster,pointer,out hit);
        if(!found||hit.gameObject.GetComponentInParent<UIEnhancementButtonHighlight>()==null)
        {
            var lines=new List<string>{$"target={target.name}; point={pointer.position}; screen={Screen.width}x{Screen.height}; camera={_camera.pixelRect}; found={found}; hit={hit.gameObject?.name}"};
            foreach(Graphic graphic in canvas.GetComponentsInChildren<Graphic>(true))
                lines.Add($"{graphic.name}: enabled={graphic.enabled}; active={graphic.gameObject.activeInHierarchy}; raycast={graphic.raycastTarget}; depth={graphic.depth}; cull={graphic.canvasRenderer.cull}; transparentCull={graphic.canvasRenderer.cullTransparentMesh}; size={graphic.rectTransform.rect.size}; alpha={graphic.color.a}; renderer={graphic.canvasRenderer.GetColor().a}");
            File.WriteAllLines(Path.Combine(_output,"raycast666.txt"),lines);
        }
        return found;
    }
    private static void Click666(GameObject hit)
    {
        var data=new PointerEventData(EventSystem.current){pointerId=-111,button=PointerEventData.InputButton.Left};
        // Same uGUI hierarchy dispatch used by the VR hand pointer; real native
        // Button invokes Awake-installed OnClick and its selected filter callback.
        ExecuteEvents.ExecuteHierarchy(hit,data,ExecuteEvents.pointerClickHandler);
    }
    private static IEnumerator Overlay666()
    {
        string[] args=Environment.GetCommandLineArgs();
        Native666.Load(args[Array.IndexOf(args,"-native666")+1],args[Array.IndexOf(args,"-rules666")+1]);
        if(EventSystem.current==null)Go("Native event system").AddComponent<EventSystem>();
        var ledger=new List<string>{"card,scale,phase,line,targetWidth,targetHeight,areaWidth,areaHeight,interactable,shaderX,shaderY"};
        foreach(int cardId in new[]{248,80})foreach(float scale in new[]{.6f,1.7f})
        {
            TownServiceMirror.Shutdown();
            Transform owner=Go("Owner666").transform,observer=Go("Observer666").transform;
            observer.SetPositionAndRotation(new Vector3(6,.13f,-.4f),Quaternion.Euler(0,83,0));observer.localScale=Vector3.one*1.4f;
            RectTransform holder=(RectTransform)Go("CardHilight",owner).transform;holder.sizeDelta=new Vector2(325,450);holder.localScale=Vector3.one*.001f;
            Canvas canvas=holder.gameObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.worldCamera=_camera;holder.gameObject.AddComponent<CanvasGroup>();
            var highlighter=holder.gameObject.AddComponent<UIEnhancementCardHighlighter>();
            var card=Go("Native pooled card",holder).AddComponent<AbilityCardUI>();
            RectTransform print=(RectTransform)Go("FullAbilityCard",card.transform).transform;print.sizeDelta=new Vector2(294,450);card.fullAbilityCard=print;highlighter.Card=card;
            CAbilityCard model=Native666.Card(cardId);
            var nativeLayout=FindSummon666(model.GetAbilityCardYML.TopActionFullLayout?.ParentGroup)??FindSummon666(model.GetAbilityCardYML.BottomActionFullLayout?.ParentGroup);
            Check(nativeLayout!=null,"original card YML resolves its actual native summon layout");
            CreateLayout.CreateSummon(nativeLayout!,print,false,cardId,card.EnhancementElements);
            SummonContainer summon=print.GetComponentInChildren<SummonContainer>();((RectTransform)summon.transform).anchoredPosition=new Vector2(0,-67);
            var cells=Targets665(summon);
            Check(cells.All(t=>t.rect.size==Vector2.zero),"actual native stat targets are zero before the first engine layout pass");
            int nativeSlots=card.EnhancementElements.All.Count;
            Check(nativeSlots==3&&card.EnhancementElements.All.All(e=>e.Ability!=null&&e.Enhancement.Enhancement==EEnhancement.NoEnhancement),"actual parsed summon creates only its three genuine free enhancement slots");
            EEnhancementLine invalid=cardId==248?EEnhancementLine.SummonRange:EEnhancementLine.SummonAttack;
            Check(!card.EnhancementElements.All.Any(e=>e.EnhancementLine==invalid),"native unavailable summon field has no fabricated enhancement entry");
            // Full-width original action target is already explicitly sized by
            // CreateLayout. Add its original model slot via native Init/Add.
            var wide=(RectTransform)Go("Row Container",print).transform;wide.sizeDelta=new Vector2(294,54);wide.pivot=new Vector2(.5f,1);wide.anchoredPosition=new Vector2(0,-251);
            var wideEnhancement=model.GetAllAbilities().Where(a=>!(a is CAbilitySummon)).SelectMany(a=>a.AbilityEnhancements).First();
            var wideButton=Go("Native wide enhancement",wide).AddComponent<EnhancementButtonBase>();wideButton.Init(wideEnhancement,"ATTACK",wide);card.EnhancementElements.Add(wideButton);
            var physical=(RectTransform)Object.Instantiate(print.gameObject,owner,false).transform;physical.name="Actual offered FullAbilityCard";physical.localScale=Vector3.one*(.00049f*scale);physical.SetPositionAndRotation(new Vector3(.17f,.81f,-.2f),Quaternion.Euler(17,43,-9));
            physical.gameObject.AddComponent<Canvas>().renderMode=RenderMode.WorldSpace;
            var printedCells=Targets665(physical.GetComponentInChildren<SummonContainer>());
            var window=new UINewEnhancementWindow{cardHolder=highlighter,highlightAbilityParent=holder};
            window.highlightAbilityPool.Add(HighlightPrefab666(holder));
            // Execute the actual native window BEFORE any engine/canvas rebuild.
            // No synthetic selected state, null ability or callback replacement.
            window.Initialise();window.Refresh();
            int materialId=0;
            TownServiceMirror.Assets.Register("fixture/native-frame-shader",Shader.Find("GVR/RectFormat666"));
            foreach(var area in window.highlightAbility)
            {
                Material material=area.transform.Find("Frame").GetComponent<Image>().material;
                Assets.Add(material);TownServiceMirror.Assets.Register("fixture/native-frame-"+(materialId++),material);
            }
            Check(window.highlightAbility.Count==4,"native availability creates three summon highlights and the genuine wide action highlight");
            foreach(var area in window.highlightAbility)
            {
                bool selected=window.enhancementLineFilter.CurrentFilter!.Ability==area.Ability.Ability&&window.enhancementLineFilter.CurrentFilter.Line==area.Ability.EnhancementLine;
                Check(area.Ability!=null&&area.GetComponent<ExtendedButton>().interactable==!selected,"native selected and selectable fields retain their authoritative interactability");
                Check(((RectTransform)area.transform).anchorMin==new Vector2(.5f,.5f)&&((RectTransform)area.transform).anchorMax==new Vector2(.5f,.5f),"native selectable retains its fixed serialized anchors");
            }
            foreach(var target in cells) { var area=Area666(target);if(area!=null)Check(((RectTransform)area.transform).rect.size==Vector2.zero,"native Highlight copies the pre-layout zero stat rectangle"); }
            var mask=card.gameObject.AddComponent<TownServiceNativeEnhancementCardMask>();TownServiceEnhancementHandoff.PhysicalCardFace=physical;
            mask.Mask();Rebuild665(summon);Rebuild665(physical.GetComponentInChildren<SummonContainer>());mask.SendMessage("OnBeforeCanvasRender");
            var offered=physical.gameObject.AddComponent<GloomhavenVR.Cards.VRCard>();
            TownServicePresentation.Ritual=new TownServiceRitual{Handoff=new TownServiceEnhancementHandoff{Card=offered,OfferedCardId=cardId,Face=physical,Zone=holder}};
            for(int phase=0;phase<3;phase++)
            {
                if(phase>0)
                {
                    summon.SummonNameText.text=physical.GetComponentInChildren<SummonContainer>().SummonNameText.text=phase==1?"Beschworener brennender Avatar":"Summon slime spirit";
                    summon.SummonNameText.canvasRenderer.SetAlpha(.8f);Rebuild665(summon);Rebuild665(physical.GetComponentInChildren<SummonContainer>());mask.SendMessage("OnBeforeCanvasRender");
                }
                Check(window.FilterCalls==1,"mask geometry refresh does not execute native selection callbacks");
                foreach(var target in cells)
                {
                    var area=Area666(target);if(area==null)continue;
                    var rectangle=(RectTransform)area.transform;var frame=area.transform.Find("Frame").GetComponent<Image>();
                    ledger.Add($"{cardId},{scale},{phase},{area.Ability.EnhancementLine},{target.rect.width:R},{target.rect.height:R},{rectangle.rect.width:R},{rectangle.rect.height:R},{area.GetComponent<ExtendedButton>().interactable},{frame.material.GetVector("_RectFormat").x:R},{frame.material.GetVector("_RectFormat").y:R}");
                    File.WriteAllLines(Path.Combine(_output,"native-selection666.csv"),ledger);
                    Check(rectangle.rect.size==target.rect.size&&rectangle.rect.width>0&&rectangle.rect.height>0,"native selectable summon rect follows its post-highlight layout target");
                    TargetCorners665(printedCells[Array.IndexOf(cells,target)],rectangle,"native summon selectable corners retain the correct printed target at both physical card scales");
                    Check(frame.material.GetVector("_RectFormat")==new Vector4(target.rect.width,target.rect.height,0,0),"native frame shader format follows the actual laid-out summon cell");
                    Check(Hit666(canvas,target,out var hit)&&hit.gameObject.GetComponentInParent<UIEnhancementButtonHighlight>()==area,"actual VR laser GraphicRaycaster reaches the genuine summon selectable");
                    Check(TownServiceEnhancementHandoff.TryNativeArea(canvas,hit.gameObject,out var accepted)&&accepted==offered,"production VR native-area validation accepts its actual initialized Ability");
                }
                Check(Area666(cells[invalid==EEnhancementLine.SummonRange?3:1])==null,"native invalid summon stat remains absent after mask and later layout");
                var wideArea=Area666(wide)!;Check(((RectTransform)wideArea.transform).rect.size==wide.rect.size,"correct full-width native action geometry remains unchanged");
                Check(summon.SummonNameText.enabled&&!Draws665(summon.SummonNameText),"native title keeps layout while its duplicate print stays masked");
            }
            foreach(var target in cells)
            {
                var area=Area666(target);if(area==null)continue;
                int callbacks=window.FilterCalls,enters=Singleton<UINavigation>.Instance.StateMachine.Enters;
                Check(Hit666(canvas,target,out var hit),"valid native summon selectable remains hittable before upgrade selection");Click666(hit.gameObject);
                if(!area.GetComponent<ExtendedButton>().interactable)
                { Check(window.FilterCalls==callbacks&&Singleton<UINavigation>.Instance.StateMachine.Enters==enters,"already-selected native field does not fire a duplicate upgrade choice");continue; }
                Check(window.FilterCalls==callbacks+1&&window.enhancementLineFilter.CurrentFilter!.Ability==area.Ability.Ability&&window.enhancementLineFilter.CurrentFilter.Line==area.Ability.EnhancementLine,"actual native click selects the genuine ability and enhancement line once");
                Check(Singleton<UINavigation>.Instance.StateMachine.Enters==enters+1,"actual native click enters its authoritative upgrade-option continuation");
            }
            // Restore native SELECTABLE state through the real shop refresh;
            // this is a real owner action, not part of the mask fit.
            window.Refresh();mask.SendMessage("OnBeforeCanvasRender");
            TownServiceSync.ResetNetwork();TownServiceSync.UseProductionPublish=true;TownServicePresentation.Active=true;TownServicePresentation.Service=3;TownServicePresentation.Session=666;
            TownServicePresentation.LocalSurfaces.Clear();TownServicePresentation.LocalSurfaces.Add(new TownServiceSurface{Id=11,Panel=new PanelFixture{Target=holder}});
            TownServiceSync.Tick(owner,owner);ushort holderId=TownServiceSync.ModuleId(holder);
            var packets=Capture();Receive(1,packets);
            for(float until=Time.unscaledTime+.4f;Time.unscaledTime<until;){TownServiceMirror.TickRemote(_=>observer);yield return null;}
            TownServiceMirror.TickRemote(_=>observer);var copy=Remote(1,holderId);Check(copy!=null,"remote native selectable original admitted through actual Sync capture codec and replay");
            string prefix="Native pooled card/FullAbilityCard/";
            foreach(var area in window.highlightAbility)
            {
                string path=Path666(print,area.transform);var remote=(RectTransform)copy!.Root.Find(prefix+path);
                Check(remote.rect.size==((RectTransform)area.transform).rect.size,"remote original retains exact complete native selectable rectangle dimensions");
                var a=new Vector3[4];var b=new Vector3[4];((RectTransform)area.transform).GetWorldCorners(a);remote.GetWorldCorners(b);
                for(int corner=0;corner<4;corner++)Check(Vector3.Distance(b[corner],observer.TransformPoint(owner.InverseTransformPoint(a[corner])))<.000025f,"remote original preserves the exact native selectable position and physical-card alignment");
                var remoteFrame=remote.Find("Frame").GetComponent<Image>();var sourceFrame=area.transform.Find("Frame").GetComponent<Image>();
                Check(remoteFrame.enabled&&remoteFrame.material.GetVector("_RectFormat")==sourceFrame.material.GetVector("_RectFormat"),"remote original retains native selectable frame draw state and exact shader format");
                if(area.Ability.EnhancementLine.ToString().StartsWith("Summon"))
                {
                    Check(LabelPixels665(sourceFrame,12,$"local-slot-{cardId}-{scale}-{area.Ability.EnhancementLine}")>100,"local native selectable submits visible frame pixels after late layout");
                    Check(LabelPixels665(remoteFrame,13,$"remote-slot-{cardId}-{scale}-{area.Ability.EnhancementLine}")>100,"remote replayed selectable submits the same visible frame pixels");
                }
                Check(remote.GetComponent<UIEnhancementButtonHighlight>()==null,"remote observer clone does not run native owner selection callbacks");
            }
            Check(LabelPixels665(summon.SummonNameText,12,$"masked-name-{cardId}-{scale}")==0,"local masked native title submits no duplicate pixels");
            mask.Restore();Check(summon.SummonNameText.enabled&&summon.SummonNameText.canvasRenderer.GetColor().a>0,"mask restores original native title draw state exactly");
            TownServiceEnhancementHandoff.PhysicalCardFace=null;TownServiceSync.ResetNetwork();TownServiceSync.UseProductionPublish=false;TownServicePresentation.Ritual=null;TownServicePresentation.LocalSurfaces.Clear();TownServiceMirror.Shutdown();Object.Destroy(owner.gameObject);Object.Destroy(observer.gameObject);yield return null;
        }
    }
    private static string Path666(Transform root,Transform node)
    { var parts=new List<string>();for(Transform current=node;current!=root;current=current.parent)parts.Insert(0,current.name);return string.Join("/",parts); }
}
