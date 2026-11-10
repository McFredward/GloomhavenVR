#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI.Surfaces;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class SnapshotChecks
{
    public static int Checks;
    public static string Output = "", Inputs = "";
    [Serializable] public sealed class Float { public string name=""; public float value; }
    [Serializable] public sealed class Tint { public string name=""; public Color value; }
    [Serializable] public sealed class Saved { public string[] keywords=Array.Empty<string>(); public int renderQueue; public Float[] floats=Array.Empty<Float>(); public Tint[] colors=Array.Empty<Tint>(); }
    private static void Check(bool condition,string reason) { Checks++; if(!condition)throw new Exception(reason); }
    private static FieldInfo SingletonField<T>() where T:MonoBehaviour => typeof(Singleton<T>).GetField("_instance",BindingFlags.Static|BindingFlags.NonPublic)!;
    private static TMP_FontAsset Font()
    {
        var field=typeof(TMP_Settings).GetField("s_Instance",BindingFlags.Static|BindingFlags.NonPublic)!;
        if(field.GetValue(null)==null)field.SetValue(null,ScriptableObject.CreateInstance<TMP_Settings>());
        var font=ScriptableObject.CreateInstance<TMP_FontAsset>();JsonUtility.FromJsonOverwrite(File.ReadAllText(Path.Combine(Inputs,"font.json")),font);
        var atlas=new Texture2D(2,2,TextureFormat.RGBA32,false);atlas.LoadImage(File.ReadAllBytes(Path.Combine(Inputs,"font-atlas.png")));
        font.atlasTextures=new[]{atlas};font.atlasPopulationMode=AtlasPopulationMode.Static;
        var material=new Material(Resources.Load<Shader>("OriginalShaders/NativeSDF"));material.mainTexture=atlas;
        var saved=JsonUtility.FromJson<Saved>(File.ReadAllText(Path.Combine(Inputs,"font-material.json")));
        material.shaderKeywords=saved.keywords;material.renderQueue=saved.renderQueue;
        foreach(var f in saved.floats)material.SetFloat(f.name,f.value);
        foreach(var c in saved.colors)material.SetColor(c.name,c.value);
        font.material=material;font.ReadFontAssetDefinition();return font;
    }
    private static RectTransform Rect(string name,Transform? parent,Vector2 size,Vector2 position)
    {
        var go=new GameObject(name,typeof(RectTransform));var rect=(RectTransform)go.transform;
        rect.SetParent(parent,false);rect.anchorMin=rect.anchorMax=rect.pivot=Vector2.one*.5f;
        rect.sizeDelta=size;rect.anchoredPosition=position;return rect;
    }
    private static GameObject Visual(TMP_FontAsset font)
    {
        var root=Rect("native informational source",null,new Vector2(360,240),Vector2.zero);
        var canvas=root.gameObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;
        root.localScale=Vector3.one*.003f;
        var backing=root.gameObject.AddComponent<Image>();backing.color=new Color(.16f,.1f,.04f,1);
        var view=Rect("viewport",root,new Vector2(270,150),new Vector2(20,-5));view.gameObject.AddComponent<RectMask2D>();
        var content=Rect("content",view,new Vector2(270,320),new Vector2(0,-43));
        var scroll=view.gameObject.AddComponent<ScrollRect>();scroll.viewport=view;scroll.content=content;scroll.horizontal=false;scroll.inertia=false;
        var list=Rect("layout",content,new Vector2(260,110),new Vector2(0,55));
        var layout=list.gameObject.AddComponent<VerticalLayoutGroup>();layout.childControlWidth=layout.childControlHeight=false;layout.childForceExpandWidth=layout.childForceExpandHeight=false;layout.spacing=7;
        for(int i=0;i<3;i++) {
            var line=Rect("row"+i,list,new Vector2(210,25),Vector2.zero);
            var text=line.gameObject.AddComponent<TextMeshProUGUI>();text.font=font;text.fontSize=22;text.text="Native card "+i;text.color=Color.white;
        }
        var icon=Rect("icon",root,new Vector2(35,35),new Vector2(-145,70));
        var texture=new Texture2D(4,4,TextureFormat.RGBA32,false);texture.SetPixels(Enumerable.Range(0,16).Select(i=>i%2==0?Color.red:Color.yellow).ToArray());texture.Apply();
        icon.gameObject.AddComponent<Image>().sprite=Sprite.Create(texture,new Rect(0,0,4,4),Vector2.one*.5f);
        // No native bootstrap is needed to author/capture the exact visual input. Native
        // controllers are attached only after this source has become inactive.
        return root.gameObject;
    }
    private static Texture2D Capture(Camera camera,string name)
    {
        Canvas.ForceUpdateCanvases();camera.Render();var old=RenderTexture.active;RenderTexture.active=camera.targetTexture;
        var image=new Texture2D(camera.targetTexture.width,camera.targetTexture.height,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,image.width,image.height),0,0);image.Apply();RenderTexture.active=old;
        File.WriteAllBytes(Path.Combine(Output,name+".png"),image.EncodeToPNG());return image;
    }
    private static void Pure(RectTransform copy,string family)
    {
        Check(copy.GetComponentsInChildren<PersistentAbilityPopup>(true).Length==0,family+" snapshot has no surviving original PersistentAbilityPopup controller");
        Check(copy.GetComponentsInChildren<UIWindow>(true).Length==0,family+" snapshot has no surviving native UIWindow lifecycle");
        Check(copy.GetComponentsInChildren<SnapshotProbe>(true).Length==0,family+" snapshot has no ordinary callback controller");
        Check(copy.GetComponentsInChildren<ScrollRect>(true).Length==0,family+" static snapshot has no scroll event emitter");
        Check(copy.GetComponentsInChildren<Animator>(true).Length==0,family+" static snapshot has no animator callbacks");
    }
    private static void SamePresentation(GameObject source,RectTransform copy,Texture2D before,Camera camera,string family)
    {
        Pure(copy,family);
        var originalRects=source.GetComponentsInChildren<RectTransform>(true);var copies=copy.GetComponentsInChildren<RectTransform>(true);
        Check(originalRects.Length==copies.Length,family+" retains the full source visual hierarchy");
        for(int i=0;i<copies.Length;i++) {
            Check(originalRects[i].anchoredPosition==copies[i].anchoredPosition && originalRects[i].sizeDelta==copies[i].sizeDelta && originalRects[i].localScale==copies[i].localScale,family+" preserves current layout and scroll content offsets");
        }
        var a=source.GetComponentsInChildren<Image>(true);var b=copy.GetComponentsInChildren<Image>(true);
        Check(a.Length==b.Length && a.Zip(b,(x,y)=>x.sprite==y.sprite&&x.material==y.material&&x.type==y.type).All(v=>v),family+" preserves original image, material and sprite identities");
        var ta=source.GetComponentsInChildren<TMP_Text>(true);var tb=copy.GetComponentsInChildren<TMP_Text>(true);
        Check(ta.Length==tb.Length&&ta.Zip(tb,(x,y)=>x.font==y.font&&x.fontSharedMaterial==y.fontSharedMaterial&&x.text==y.text).All(v=>v),family+" preserves all TMP font/material/text identities");
        var inactiveHome=copy.parent;
        var activeHost=new GameObject(family+" conversion activation host");
        // Real CanvasConversion reparents an already activeSelf shown target into an
        // active host. It does not force native visibility. Preserve that exact boundary.
        copy.SetParent(activeHost.transform,false);
        var after=Capture(camera,family);
        Check(before.GetPixels32().SequenceEqual(after.GetPixels32()),family+" final layout/text/image pixels equal the exact source snapshot");
        copy.SetParent(inactiveHome,false); // existing release boundary, before destruction
    }
    public static IEnumerator Run()
    {
        Checks=0;SnapshotProbe.Awakes=0;SnapshotFault.RefuseProbe=false;VRLog.Refusals=0;
        var camera=new GameObject("proof eye",typeof(Camera)).GetComponent<Camera>();camera.enabled=false;
        camera.transform.position=new Vector3(0,0,-5);camera.orthographic=true;camera.orthographicSize=.5f;camera.aspect=1.5f;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.targetTexture=new RenderTexture(600,400,24);camera.targetTexture.Create();
        var source=Visual(Font());var sourceHome=new GameObject("native source hierarchy");source.transform.SetParent(sourceHome.transform,false);yield return null;yield return null;
        var baseline=Capture(camera,"source");Check(baseline.GetPixels32().Count(p=>p.r>150&&p.g>150&&p.b>150)>30,"real native font renders legible glyph pixels");
        sourceHome.SetActive(false); // shown native target stays activeSelf, as in real snapshots
        var actor=source.AddComponent<ActorStatPanel>();var prop=source.AddComponent<UITextInfoPanel>();
        source.AddComponent<SnapshotProbe>();source.AddComponent<Animator>();
        var popup=Rect("native PersistentAbilityPopup",source.transform,new Vector2(20,20),Vector2.zero);
        popup.gameObject.AddComponent<PersistentAbilityPopup>();
        var actorField=SingletonField<ActorStatPanel>();var propField=SingletonField<UITextInfoPanel>();
        var previousActor=actorField.GetValue(null);var previousProp=propField.GetValue(null);
        actorField.SetValue(null,actor);propField.SetValue(null,prop);
        var nativeWindow=source.GetComponent<UIWindow>();int registered=UIWindow.GetWindows().Count;
        var sourceComponents=source.GetComponentsInChildren<Component>(true).Select(c=>c.GetInstanceID()).ToArray();
        try {
            var copy=StatPanelSurface.Snapshot(actor);Check(copy!=null,"figure snapshot is admitted after stripping");
            SamePresentation(source,copy!,baseline,camera,"figure");
            Check(ReferenceEquals(actorField.GetValue(null),actor)&&UIWindow.GetWindows().Count==registered,"figure activation keeps original singleton and window registry unchanged");
            StatPanelSurface.Reset();yield return null;
            var propCopy=PropInfoSurface.Snapshot("first held prop");Check(propCopy!=null,"prop snapshot is admitted after stripping");
            SamePresentation(source,propCopy!,baseline,camera,"prop");
            PropInfoSurface.DropSecondCard("earlier prop released");yield return null;
            Check(SnapshotProbe.Awakes==0,"no cloned controller Awake callback ever runs");
            Check(ReferenceEquals(actorField.GetValue(null),actor)&&ReferenceEquals(propField.GetValue(null),prop)&&UIWindow.GetWindows().Count==registered,"both clone lifecycles preserve original native singleton/window state");
            // Fault only the destruction endpoint in the bound helper, as if Unity refused
            // one unknown component. Both real admission consumers and latches still execute.
            SnapshotFault.RefuseProbe=true;int refusals=VRLog.Refusals;
            Check(StatPanelSurface.Snapshot(actor)==null,"rejected figure snapshot never enters conversion admission");
            for(int i=0;i<5;i++)Check(StatPanelSurface.Snapshot(actor)==null,"same refused figure hold remains absent");
            Check(VRLog.Refusals==refusals+1,"same refused figure hold is built/reported only once");
            StatPanelSurface.NewHold();StatPanelSurface.Snapshot(actor);
            Check(VRLog.Refusals==refusals+2,"figure release/reacquire permits one new attempt");
            var replacementHolder=new GameObject("new native source holder");replacementHolder.SetActive(false);
            var replacement=Object.Instantiate(source,replacementHolder.transform,false);var nextActor=replacement.GetComponent<ActorStatPanel>();
            StatPanelSurface.Snapshot(nextActor);Check(VRLog.Refusals==refusals+3,"replacement native figure source permits one new attempt");
            refusals=VRLog.Refusals;
            Check(PropInfoSurface.Snapshot("refused prop")==null,"rejected prop snapshot never enters conversion admission");
            for(int i=0;i<5;i++)Check(PropInfoSurface.Snapshot("refused prop")==null,"same refused prop hold remains absent");
            Check(VRLog.Refusals==refusals+1,"same refused prop hold is built/reported only once");
            PropInfoSurface.DropSecondCard("earlier prop released");PropInfoSurface.Snapshot("refused prop");
            Check(VRLog.Refusals==refusals+2,"prop release/reacquire permits one new attempt");
            propField.SetValue(null,replacement.GetComponent<UITextInfoPanel>());PropInfoSurface.Snapshot("refused prop");
            Check(VRLog.Refusals==refusals+3,"replacement native prop source permits one new attempt");
            propField.SetValue(null,prop);SnapshotFault.RefuseProbe=false;StatPanelSurface.NewHold();PropInfoSurface.DropSecondCard("new hold");
            Check(StatPanelSurface.Snapshot(actor)!=null && PropInfoSurface.Snapshot("recovered prop")!=null,"new holds recover after a refused dependency disappears");
            var active=new GameObject("active input",typeof(SnapshotProbe));int count=active.GetComponents<Component>().Length;
            Check(!StatPanelSurface.StripLogicComponents(active,out int stripped)&&stripped==0&&active.GetComponents<Component>().Length==count,"active input is rejected without stripping or deactivating its original source");
            Check(source.GetComponentsInChildren<Component>(true).Select(c=>c.GetInstanceID()).SequenceEqual(sourceComponents),"neither consumer mutates any original source component identity");
            Check(nativeWindow!=null&&!source.activeInHierarchy,"original native window remains intact and unactivated");
        }
        finally {
            SnapshotFault.RefuseProbe=false;StatPanelSurface.Reset();PropInfoSurface.DropSecondCard("fixture cleanup");
            actorField.SetValue(null,previousActor);propField.SetValue(null,previousProp);
        }
        yield return null;
    }
}
