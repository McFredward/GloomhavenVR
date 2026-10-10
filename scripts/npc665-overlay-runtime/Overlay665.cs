using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static partial class MirrorProgram
{
    private static Vector2 V2665(BinaryReader r) => new(r.ReadSingle(),r.ReadSingle());
    private static Vector3 V3665(BinaryReader r) => new(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());
    private static Color C665(BinaryReader r) => new(r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),r.ReadSingle());
    private static string S665(BinaryReader r) => System.Text.Encoding.UTF8.GetString(r.ReadBytes(r.ReadUInt16()));
    private static SummonContainer Summon665(Transform parent)
    {
        string[] args=Environment.GetCommandLineArgs();
        using var r=new BinaryReader(File.OpenRead(args[Array.IndexOf(args,"-nativeSummon665")+1]));
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
                { var image=node.gameObject.AddComponent<Image>();image.color=C665(r);image.enabled=enabled; }
                else if(type==7)
                { summon=node.gameObject.AddComponent<SummonContainer>();references=new int[6];for(int j=0;j<6;j++)references[j]=r.ReadInt16();summon.enabled=enabled; }
                else throw new InvalidDataException("Unknown serialized layout component");
            }
            node.gameObject.SetActive(active);
        }
        Check(r.BaseStream.Position==r.BaseStream.Length,"native summon importer consumes exact serialized hierarchy");
        summon!.SummonNameText=nodes[references![0]].GetComponent<TMP_Text>();
        summon.SummonLT=nodes[references[1]].gameObject;summon.SummonLB=nodes[references[2]].gameObject;
        summon.SummonMT=nodes[references[3]].gameObject;summon.SummonMB=nodes[references[4]].gameObject;summon.SummonR=nodes[references[5]].gameObject;
        return summon;
    }
    private static RectTransform[] Targets665(SummonContainer summon) => new[] { (RectTransform)summon.SummonLT.transform,(RectTransform)summon.SummonLB.transform,(RectTransform)summon.SummonMT.transform,(RectTransform)summon.SummonMB.transform };
    private static void Rebuild665(SummonContainer summon)
    { Canvas.ForceUpdateCanvases();LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)summon.transform);Canvas.ForceUpdateCanvases(); }
    private static UIEnhancementButtonHighlight Highlight665(RectTransform target)
    {
        RectTransform root=(RectTransform)Go("Enhancement Ability Highlight Variant",target).transform;
        // The game's native pool uses SetParent(target) with worldPositionStays.
        root.SetAsFirstSibling();root.localScale=Vector3.one;root.localRotation=Quaternion.identity;
        var fill=root.gameObject.AddComponent<CanvasGroup>();
        Image ink=Image("Native frame",root,Vector2.zero,target.rect.size,Color.cyan);
        var button=root.gameObject.AddComponent<UIEnhancementButtonHighlight>();button.Native665(target,ink,fill);return button;
    }
    private static void TargetCorners665(RectTransform expected,RectTransform actual,string invariant)
    {
        var a=new Vector3[4];var b=new Vector3[4];expected.GetWorldCorners(a);actual.GetWorldCorners(b);
        RectTransform print=(RectTransform)expected.GetComponentInParent<Canvas>().transform;
        Vector3 bias=-print.forward*(print.rect.height*print.TransformVector(Vector3.up).magnitude*.0003f);
        for(int i=0;i<4;i++)Check(Vector3.Distance(a[i]+bias,b[i])<.000015f,invariant);
    }
    private static int LabelPixels665(TMP_Text text,int layer,string name)
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
    private static IEnumerator Overlay665()
    {
        var ledger=new List<string>{"scale,phase,target,expectedY,actualY,labelEnabled,labelRendererAlpha"};
        foreach(float scale in new[]{.6f,1.7f})
        foreach(int initialState in new[]{0,1,2})
        {
            TownServiceMirror.Shutdown();
            Transform owner=Go("Owner665").transform,observer=Go("Observer665").transform;
            observer.SetPositionAndRotation(new Vector3(6,.13f,-.4f),Quaternion.Euler(0,83,0));observer.localScale=Vector3.one*1.4f;
            RectTransform holder=(RectTransform)Go("CardHilight",owner).transform;holder.sizeDelta=new Vector2(325,450);holder.localScale=Vector3.one*.001f;
            holder.gameObject.AddComponent<Canvas>().renderMode=RenderMode.WorldSpace;holder.gameObject.AddComponent<CanvasGroup>();
            var highlighter=holder.gameObject.AddComponent<UIEnhancementCardHighlighter>();
            var card=Go("Native pooled card",holder).AddComponent<AbilityCardUI>();
            RectTransform print=(RectTransform)Go("FullAbilityCard",card.transform).transform;print.sizeDelta=new Vector2(294,450);card.fullAbilityCard=print;highlighter.Card=card;
            SummonContainer native=Summon665(print);((RectTransform)native.transform).anchoredPosition=new Vector2(0,-67);
            Rebuild665(native);
            RectTransform broad=(RectTransform)Go("Row Container",print).transform;broad.sizeDelta=new Vector2(294,54);broad.pivot=new Vector2(.5f,1);broad.anchoredPosition=new Vector2(0,-251);
            // Physical face is the original enabled native print, adopted by CardFace;
            // adoption itself is a declared fixture boundary, not a second layout.
            RectTransform physical=(RectTransform)Object.Instantiate(print.gameObject,owner,false).transform;physical.name="Actual offered FullAbilityCard";
            physical.localScale=Vector3.one*(.00049f*scale);physical.SetPositionAndRotation(new Vector3(.17f,.81f,-.2f),Quaternion.Euler(17,43,-9));
            physical.gameObject.AddComponent<Canvas>().renderMode=RenderMode.WorldSpace;
            SummonContainer original=physical.GetComponentInChildren<SummonContainer>();Rebuild665(original);
            RectTransform[] cells=Targets665(native),expected=Targets665(original);
            var highlights=new UIEnhancementButtonHighlight[5];for(int i=0;i<4;i++)highlights[i]=Highlight665(cells[i]);highlights[4]=Highlight665(broad);
            var mask=card.gameObject.AddComponent<TownServiceNativeEnhancementCardMask>();
            if(initialState==1) { native.gameObject.SetActive(false);original.gameObject.SetActive(false); }
            if(initialState==2) { native.SummonNameText.enabled=false;original.SummonNameText.enabled=false; }
            Color originalRenderer=native.SummonNameText.canvasRenderer.GetColor();bool originalEnabled=native.SummonNameText.enabled;
            TownServiceEnhancementHandoff.PhysicalCardFace=physical;mask.Mask();mask.SendMessage("LateUpdate");
            if(initialState==1) {
                Check(native.SummonNameText.enabled==originalEnabled&&!native.gameObject.activeInHierarchy&&native.SummonNameText.canvasRenderer.GetColor().a==0,"inactive native summon branch retains its original layout enabled state and cannot draw");
                native.gameObject.SetActive(true);original.gameObject.SetActive(true);
            }
            if(initialState==2)Check(!native.SummonNameText.enabled,"originally disabled native summon label remains disabled while masked");
            for(int refresh=0;refresh<3;refresh++)
            {
                if(refresh>0)
                {
                    // Genuine native text refresh and renderer tween writes after Mask.
                    native.SummonNameText.text=original.SummonNameText.text=refresh==1?"Summon Thing":"Beschworener brennender Avatar";
                    native.SummonNameText.color=new Color(.75f,.65f,.55f,.85f);
                    native.SummonNameText.canvasRenderer.SetColor(new Color(.4f,.6f,.8f,.65f));
                }
                Rebuild665(native);Rebuild665(original);
                for(int i=0;i<4;i++)highlights[i].Native665(cells[i],highlights[i].transform.GetChild(0).GetComponent<Image>(),highlights[i].GetComponent<CanvasGroup>());
                highlights[4].Native665(broad,highlights[4].transform.GetChild(0).GetComponent<Image>(),highlights[4].GetComponent<CanvasGroup>());
                mask.SendMessage("OnBeforeCanvasRender");
                TargetCorners665((RectTransform)physical.Find("Row Container"),(RectTransform)highlights[4].transform,"correct full-width row geometry remains unchanged");
                for(int i=0;i<4;i++)
                {
                    float wanted=physical.InverseTransformPoint(expected[i].position).y,actual=print.InverseTransformPoint(cells[i].position).y;
                    ledger.Add($"{scale},{initialState}-{refresh},{cells[i].name},{wanted:R},{actual:R},{native.SummonNameText.enabled},{native.SummonNameText.canvasRenderer.GetColor().a:R}");
                }
                File.WriteAllLines(Path.Combine(_output,"layout665.csv"),ledger);
                for(int i=0;i<4;i++)
                {
                    float wanted=physical.InverseTransformPoint(expected[i].position).y,actual=print.InverseTransformPoint(cells[i].position).y;
                    Check(Mathf.Abs(actual-wanted)<.001f,"native summon stat target retains its original label layout while printed artwork is masked");
                    TargetCorners665(expected[i],(RectTransform)highlights[i].transform,"local native summon highlight corners match the exact printed stat target after layout rebuild and native refresh");
                }
                Check(native.SummonNameText.enabled==originalEnabled&&!Draws665(native.SummonNameText),"masked native summon label preserves layout activity without drawing pooled artwork");
                Check(original.SummonNameText.enabled==originalEnabled&&Draws665(original.SummonNameText)==originalEnabled,"actual physical original summon label retains its genuine visible or disabled state");
            }
            // Actual publication/capture, codec, original validation and inert replay.
            TownServiceSync.ResetNetwork();TownServiceSync.UseProductionPublish=true;
            TownServicePresentation.Active=true;TownServicePresentation.Service=3;TownServicePresentation.Session=665;
            TownServicePresentation.LocalSurfaces.Clear();
            TownServicePresentation.LocalSurfaces.Add(new TownServiceSurface { Id=11,Panel=new PanelFixture { Target=holder } });
            TownServicePresentation.Ritual=new TownServiceRitual { Handoff=new TownServiceEnhancementHandoff {
                Card=physical.gameObject.AddComponent<GloomhavenVR.Cards.VRCard>(),OfferedCardId=665,Face=physical,Zone=holder } };
            TownServiceSync.Tick(owner,owner);
            Check(TownServiceSync.HasPublishedSource(holder)&&TownServiceSync.HasPublishedSource(physical),"actual Sync routes native holder and exact offered original through production Publish registration");
            ushort holderId=TownServiceSync.ModuleId(holder);
            var packets=Capture();Receive(1,packets);
            for(float until=Time.unscaledTime+.4f;Time.unscaledTime<until;){TownServiceMirror.TickRemote(_=>observer);yield return null;}
            var copy=Remote(1,holderId);Check(copy!=null,"remote native summon original admitted through real capture and codec");
            string prefix="Native pooled card/FullAbilityCard/";
            var copiedName=copy!.Root.Find(prefix+"SummonContainer/SummonName").GetComponent<TMP_Text>();
            Check(copiedName.enabled==originalEnabled&&!Draws665(copiedName)&&copiedName.canvasRenderer.GetColor().a==0,"remote original renderer stream suppresses pooled summon text while retaining enabled layout provider");
            Check(LabelPixels665(native.SummonNameText,12,$"native-pooled-{scale}-{initialState}")==0,"local masked native summon name submits zero visible pixels");
            Check(LabelPixels665(copiedName,13,$"remote-pooled-{scale}-{initialState}")==0,"remote replayed masked native summon name submits zero visible pixels");
            int actualPixels=LabelPixels665(original.SummonNameText,14,$"physical-original-{scale}-{initialState}");
            Check(originalEnabled?actualPixels>100:actualPixels==0,"physical original native summon name retains visible ink or genuine native disablement");
            for(int i=0;i<5;i++)
            {
                string path=Path665(print,highlights[i].transform);var remoteArea=(RectTransform)copy.Root.Find(prefix+path);
                var a=new Vector3[4];var b=new Vector3[4];highlights[i].GetComponent<RectTransform>().GetWorldCorners(a);remoteArea.GetWorldCorners(b);
                for(int corner=0;corner<4;corner++)Check(Vector3.Distance(b[corner],observer.TransformPoint(owner.InverseTransformPoint(a[corner])))<.000025f,i<4?"remote narrow summon rectangles preserve corrected original local geometry":"remote full-width action rectangle retains its correct original geometry");
                Check(remoteArea.GetChild(0).GetComponent<Image>().enabled,i<4?"remote native small rectangle ink remains enabled":"remote full-width action rectangle ink remains enabled");
            }
            mask.Restore();
            Check(native.SummonNameText.enabled==originalEnabled&&native.SummonNameText.color==new Color(.75f,.65f,.55f,.85f)&&native.SummonNameText.canvasRenderer.GetColor()==originalRenderer,"mask restore returns original native summon enabled and renderer colour exactly");
            // Re-pooling starts a new exact renderer snapshot, without forcing
            // a previously disabled native label on during either mask lifetime.
            Color pooled=new Color(.25f,.45f,.65f,.37f);native.SummonNameText.color=pooled;native.SummonNameText.canvasRenderer.SetColor(pooled);
            mask.Mask();mask.SendMessage("OnBeforeCanvasRender");
            Check(native.SummonNameText.enabled==originalEnabled&&native.SummonNameText.canvasRenderer.GetColor().a==0,"re-pooled native summon label remains correctly masked without changing layout activity");
            mask.Restore();Check(native.SummonNameText.enabled==originalEnabled&&native.SummonNameText.color==pooled&&native.SummonNameText.canvasRenderer.GetColor()==pooled,"re-pooled native summon label restores its new exact renderer snapshot");
            TownServiceEnhancementHandoff.PhysicalCardFace=null;TownServiceSync.ResetNetwork();TownServiceSync.UseProductionPublish=false;TownServicePresentation.Ritual=null;TownServicePresentation.LocalSurfaces.Clear();TownServiceMirror.Shutdown();Object.Destroy(owner.gameObject);Object.Destroy(observer.gameObject);yield return null;
        }
    }
    private static string Path665(Transform root,Transform node)
    { var parts=new List<string>();for(Transform current=node;current!=root;current=current.parent)parts.Insert(0,current.name);return string.Join("/",parts); }
}
