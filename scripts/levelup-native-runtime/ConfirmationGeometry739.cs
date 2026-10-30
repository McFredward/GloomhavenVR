extern alias native;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Xml;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.Surfaces;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

public static partial class LevelUpNativeProgram
{
    [Serializable] private sealed class NativeCanvasGroup748
    {
        public int m_Enabled=1;public float m_Alpha=1f;
        public bool m_Interactable=true,m_BlocksRaycasts=true,m_IgnoreParentGroups=false;
    }
    private static TMP_FontAsset Font739(string prefix)
    {
        var font=ScriptableObject.CreateInstance<TMP_FontAsset>();JsonUtility.FromJsonOverwrite(File.ReadAllText(Path.Combine(_inputs,prefix+".json")),font);
        var atlas=LoadTexture(prefix+"-atlas");font.atlasTextures=new[]{atlas};font.atlasPopulationMode=AtlasPopulationMode.Static;
        var material=new Material(Resources.Load<Shader>("OriginalShaders/NativeSDF"));material.mainTexture=atlas;
        var saved=JsonUtility.FromJson<SavedMaterial>(File.ReadAllText(Path.Combine(_inputs,prefix+"-material.json")));
        material.shaderKeywords=saved.keywords;material.renderQueue=saved.renderQueue;
        foreach(var item in saved.floats)material.SetFloat(item.name,item.value);
        foreach(var item in saved.colors)material.SetColor(item.name,item.value);
        font.material=material;font.ReadFontAssetDefinition();return font;
    }
    private static float[] Values739(XmlElement item,string key)
        =>Array.ConvertAll(item.GetAttribute(key).Split(','),s=>float.Parse(s,CultureInfo.InvariantCulture));
    private static Vector2 Pair739(XmlElement item,string key){float[] p=Values739(item,key);return new Vector2(p[0],p[1]);}
    private static Vector3 Triple739(XmlElement item,string key){float[] p=Values739(item,key);return new Vector3(p[0],p[1],p[2]);}
    private static RectTransform ImportNode739(XmlElement node,Transform parent,Dictionary<string,TMP_FontAsset> fonts,bool narrative=false,
        Dictionary<string,Texture2D>? textures=null,List<Object>? owned=null)
    {
        string name=node.GetAttribute("name");var rect=Rect(name,parent,0,0);rect.gameObject.SetActive(false);
        foreach(XmlElement part in node.SelectNodes("component"))
        {
            string kind=part.GetAttribute("kind"),json=part.GetAttribute("json");
            if(kind=="RectTransform")
            {
                rect.anchorMin=Pair739(part,"m_AnchorMin");rect.anchorMax=Pair739(part,"m_AnchorMax");
                rect.pivot=Pair739(part,"m_Pivot");rect.sizeDelta=Pair739(part,"m_SizeDelta");rect.anchoredPosition=Pair739(part,"m_AnchoredPosition");
                rect.localPosition=new Vector3(rect.localPosition.x,rect.localPosition.y,Triple739(part,"m_LocalPosition").z);
                float[] q=Values739(part,"m_LocalRotation");rect.localRotation=new Quaternion(q[0],q[1],q[2],q[3]);rect.localScale=Triple739(part,"m_LocalScale");
            }
            else if(kind=="Image")
            {
                var image=rect.gameObject.AddComponent<Image>();JsonUtility.FromJsonOverwrite(json,image);
                if(part.HasAttribute("sprite"))
                {
                    string key=part.GetAttribute("sprite");Texture2D texture;
                    if(textures==null)texture=LoadTexture(key);
                    else if(!textures.TryGetValue(key,out texture)){texture=LoadTexture(key);textures.Add(key,texture);owned?.Add(texture);}
                    float[] pivot=Values739(part,"pivot"),border=Values739(part,"border");
                    image.sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(pivot[0],pivot[1]),
                        float.Parse(part.GetAttribute("ppu"),CultureInfo.InvariantCulture),0,SpriteMeshType.FullRect,new Vector4(border[0],border[1],border[2],border[3]));
                    image.sprite.name=part.GetAttribute("spriteName");
                    owned?.Add(image.sprite);
                }
                string shader=part.GetAttribute("shader");
                if(shader=="Custom/SimpleGrabPassBlur"){image.material=new Material(Resources.Load<Shader>("ConfirmationGrab738"));owned?.Add(image.material);}
                else if(!narrative) Check(shader=="UI/Default","native confirmation foreground retains the original stock UI material family");
            }
            else if(kind=="TextMeshProUGUI")
            {
                var text=rect.gameObject.AddComponent<TextMeshProUGUI>();JsonUtility.FromJsonOverwrite(json,text);
                string prefix=part.GetAttribute("font");if(!fonts.TryGetValue(prefix,out TMP_FontAsset font))
                {
                    font=Font739(prefix);fonts.Add(prefix,font);
                    if(owned!=null){owned.Add(font.material);foreach(Texture2D atlas in font.atlasTextures)owned.Add(atlas);owned.Add(font);}
                }
                text.font=font;text.fontSharedMaterial=font.material;
            }
            else
            {
                Type? type=kind=="VerticalLayoutGroup"?typeof(VerticalLayoutGroup):kind=="HorizontalLayoutGroup"?typeof(HorizontalLayoutGroup)
                    :kind=="ContentSizeFitter"?typeof(ContentSizeFitter):kind=="LayoutElement"?typeof(LayoutElement):null;
                if(narrative&&type==null)
                    type=kind=="VerticalLayoutGroupExtended"?typeof(native::UnityEngine.UI.VerticalLayoutGroupExtended)
                        :kind=="HorizontalLayoutGroupExtended"?typeof(native::UnityEngine.UI.HorizontalLayoutGroupExtended)
                        :kind=="LayoutElementExtended"?typeof(native::UnityEngine.UI.LayoutElementExtended)
                        :kind=="AspectRatioFitter"?typeof(AspectRatioFitter):kind=="RectMask2D"?typeof(RectMask2D):kind=="CanvasGroup"?typeof(CanvasGroup):null;
                if(type==typeof(CanvasGroup))
                {
                    var authored=JsonUtility.FromJson<NativeCanvasGroup748>(json);var group=rect.gameObject.AddComponent<CanvasGroup>();
                    group.enabled=authored.m_Enabled!=0;group.alpha=authored.m_Alpha;group.interactable=authored.m_Interactable;
                    group.blocksRaycasts=authored.m_BlocksRaycasts;group.ignoreParentGroups=authored.m_IgnoreParentGroups;
                }
                else if(type!=null)JsonUtility.FromJsonOverwrite(json,rect.gameObject.AddComponent(type));
            }
        }
        rect.name=name;
        foreach(XmlElement child in node.SelectNodes("node"))ImportNode739(child,rect,fonts,narrative,textures,owned);
        rect.gameObject.SetActive(node.GetAttribute("active")=="1");return rect;
    }
    private static int ExteriorDifference739(Texture2D actual,Texture2D reference,Camera head,RectTransform small)
    {
        var corners=new Vector3[4];small.GetWorldCorners(corners);float minX=float.PositiveInfinity,minY=minX,maxX=float.NegativeInfinity,maxY=maxX;
        foreach(Vector3 corner in corners){Vector3 p=head.WorldToScreenPoint(corner);minX=Mathf.Min(minX,p.x);maxX=Mathf.Max(maxX,p.x);minY=Mathf.Min(minY,p.y);maxY=Mathf.Max(maxY,p.y);}
        var a=actual.GetPixels32();var b=reference.GetPixels32();int changed=0;
        for(int i=0;i<a.Length;i++)
        {int x=i%actual.width,y=i/actual.width;if(x>=minX-2&&x<=maxX+2&&y>=minY-2&&y<=maxY+2)continue;
            if(Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b)>2)changed++;}
        return changed;
    }
    private static IEnumerator ConfirmationGeometry739(Camera head,RectTransform ownerRoot,UILevelUpWindow owner,string output)
    {
        Vector3 rootPosition=ownerRoot.position,rootScale=ownerRoot.localScale;Quaternion rootRotation=ownerRoot.rotation;
        Vector3 cameraPosition=head.transform.position;Quaternion cameraRotation=head.transform.rotation;Color clear=head.backgroundColor;
        bool ortho=head.orthographic;float near=head.nearClipPlane,far=head.farClipPlane,fov=head.fieldOfView;int mask=head.cullingMask;
        UIConfirmationBoxManager? previousManager=Singleton<UIConfirmationBoxManager>.Instance;
        float previousScale=PanelLayout.WorldScale;PanelLayout.WorldScale=198.12f;
        var ownerPanel=CanvasConversion.ActivePanels.Find(p=>ReferenceEquals(p.Target,ownerRoot))!;bool ownerCanvasEnabled=ownerPanel.HostCanvas.enabled;int oldBase=ownerPanel.BaseSortingOrder;ownerPanel.BaseSortingOrder=1000;
        var ownerInk=Image(ownerRoot,"original owner photograph overlap",1920,1080,new Color(.42f,.11f,.07f,1));ownerInk.transform.SetAsFirstSibling();
        var home=Rect("native confirmation import home",null,1920,1080);Object.DontDestroyOnLoad(home.gameObject);
        var sameHome=Rect("native same-scene confirmation home",null,1920,1080);SceneManager.MoveGameObjectToScene(sameHome.gameObject,ownerRoot.gameObject.scene);
        var perksWindow=Rect("native perks window",ownerRoot,1620,1080);perksWindow.anchoredPosition=new Vector2(47,-18);perksWindow.gameObject.AddComponent<UIWindow>();
        var perksContent=Rect("native perks inventory",perksWindow,418,1080);var perks=perksContent.gameObject.AddComponent<UIPerksInventory>();
        var document=new XmlDocument();document.Load(Path.Combine(_inputs,"native-confirmation-tree.xml"));
        var imported=ImportNode739(document.DocumentElement!,home,new Dictionary<string,TMP_FontAsset>());
        var hierarchy=new List<string>();foreach(Transform t in imported.GetComponentsInChildren<Transform>(true))hierarchy.Add(t.name+" parent="+(t.parent==null?"null":t.parent.name)+" components="+string.Join(",",Array.ConvertAll(t.GetComponents<Component>(),c=>c.GetType().Name)));File.WriteAllLines(Path.Combine(output,"native-confirmation-import739.txt"),hierarchy);
        var small=(RectTransform)imported.Find("Box");var backdrop=imported.GetComponent<Image>();var blur=imported.Find("Blur").GetComponent<Image>();
        // Original ResetConfirmationBox toggles these modes. The translated
        // question is an explicit content input; original layout/graphics execute.
        imported.Find("Box/Box Content/Swappable Content/Secondary Content").gameObject.SetActive(false);
        imported.Find("Box/Box Content/Swappable Content/Secondary General Content").gameObject.SetActive(false);
        imported.Find("Box/Box Content/Report content").gameObject.SetActive(false);
        imported.Find("Box/Box Content/Swappable Content/Primary Content/Text").GetComponent<TMP_Text>().text="Take this ability card?";
        Canvas.ForceUpdateCanvases();LayoutRebuilder.ForceRebuildLayoutImmediate(imported);Canvas.ForceUpdateCanvases();
        var box=imported.gameObject.AddComponent<ConfirmationBox>();var window=imported.gameObject.AddComponent<UIWindow>();
        var manager=new UIConfirmationBoxManager{CurrentBox=box};Singleton<UIConfirmationBoxManager>.Instance=manager;
        ownerRoot.SetPositionAndRotation(new Vector3(267.92f,174.92f,58.59f),Quaternion.Euler(0,37,0));ownerRoot.localScale=Vector3.one*.08275f;
        head.orthographic=false;head.nearClipPlane=1;head.farClipPlane=2000;head.fieldOfView=60;head.cullingMask=(1<<5)|(1<<26);
        head.backgroundColor=new Color(.12f,.27f,.4f,1);var rows=new List<string>();var failures=new List<(string Label,bool Value)>();
        try
        {
            foreach(string route in new[]{"owned","perks","perks-same-scene","standalone"})
            {
                RectTransform expectedHome=route=="perks-same-scene"?sameHome:home;
                imported.SetParent(expectedHome,false);int nativeScene=imported.gameObject.scene.handle;
                box.IsOpen=window.IsOpen=window.IsVisible=true;DialogSurface? surface=null;ownerPanel.HostCanvas.enabled=route!="standalone"&&ownerCanvasEnabled;
                if(route!="standalone"){Record(manager,route=="owned"?(Component)owner:perks);ConfirmationOnOwner.Tick();
                    Check(ConfirmationOnOwner.IsOwned(window),"every perks confirmation is a foreground member of its actual producer window");
                    ConfirmationAA745(window,route=="owned"?owner.transform:perksWindow,route);}
                else {var scope=ConfirmationOnOwner.CurrentScope;ConfirmationOnOwner.CurrentScope=null;ConfirmationOnOwner.Record(manager);ConfirmationOnOwner.CurrentScope=scope;surface=new DialogSurface();surface.Tick();}
                var panel=CanvasConversion.ActivePanels.Find(p=>ReferenceEquals(p.Target,imported))!;
                if(route=="standalone"){panel.HostRect.SetPositionAndRotation(ownerRoot.position,ownerRoot.rotation);panel.HostRect.localScale=ownerRoot.lossyScale;}
                CanvasConversion.ResetOrder739();
                // Captures are deliberately absent: fresh738 logs report negative
                // remaining session capacity and this exact direct rendering route.
                Check(small.rect.width>300&&small.rect.height>60&&small.rect.height<1080,"original native layout resolves a small dialog rather than the full-screen frame");
                bool ownerVisible=ownerPanel.HostCanvas.enabled;ownerPanel.HostCanvas.enabled=false;
                head.transform.position=small.position-panel.HostRect.forward*120;head.transform.rotation=Quaternion.LookRotation(small.position-head.transform.position,panel.HostRect.up);
                Texture2D nativeOnly=Capture(head,Path.Combine(output,route+"-direct-native-only739.png"));
                Check(BrightPixels(nativeOnly)>30,"native confirmation alone retains visible original glyphs without owner content");
                Object.DestroyImmediate(nativeOnly);ownerPanel.HostCanvas.enabled=ownerVisible;

                for(int frame=0;frame<10;frame++)
                {
                    if(route!="standalone")
                    {
                        ownerRoot.SetPositionAndRotation(new Vector3(267.92f+frame*.13f,174.92f-frame*.04f,58.59f),Quaternion.Euler(0,37+frame*.11f,0));
                        ownerRoot.localScale=Vector3.one*(.08275f+frame*.00001f);ConfirmationOnOwner.Tick();
                        var producerMount=route=="owned"?(RectTransform)owner.transform:perksWindow;
                        Check(Vector3.Distance(panel.HostRect.position,producerMount.TransformPoint(new Vector3(0,0,-32)))<.0001f
                            && Vector3.Distance(panel.HostRect.lossyScale,producerMount.lossyScale)<.000001f,
                            "perks and level-up foreground keep exact owner geometry and ordering through accepted drag and resize");
                        Check(imported.gameObject.scene.handle==nativeScene&&!imported.IsChildOf(ownerRoot),
                            "same and cross-scene confirmations retain original native lifetime while following their owner");
                    }
                    CanvasConversion.SweepBackgrounds739(panel);
                    for(int side=0;side<2;side++)for(int eye=0;eye<2;eye++)
                    {
                        var mount=panel.HostRect;Vector3 centre=small.position;float distance=side==0?-120:120;
                        head.transform.position=centre+mount.forward*distance+mount.right*((eye==0?-6.34f:6.34f)+frame*.023f)+mount.up*Mathf.Sin(frame*.6f)*.13f;
                        head.transform.rotation=Quaternion.LookRotation(centre-head.transform.position,mount.up);
                        CanvasConversion.TickPanelOrder();
                        string stem=route+"-direct-"+frame+"-"+side+"-"+eye;
                        Texture2D actual=Capture(head,frame==0||frame==9?Path.Combine(output,stem+".png"):"");
                        bool backingEnabled=backdrop.enabled,blurEnabled=blur.enabled;backdrop.enabled=blur.enabled=false;
                        int originalOrder=panel.HostCanvas.sortingOrder;panel.HostCanvas.sortingOrder=ownerPanel.HostCanvas.sortingOrder+16;
                        Texture2D reference=Capture(head,frame==0?Path.Combine(output,stem+"-reference.png"):"");
                        panel.HostCanvas.sortingOrder=originalOrder;
                        backdrop.enabled=backingEnabled;blur.enabled=blurEnabled;
                        float difference=Difference(actual,reference);int exterior=ExteriorDifference739(actual,reference,head,small);
                        rows.Add(stem+","+difference.ToString("R")+","+exterior+","+BrightPixels(actual)+","+BrightPixels(reference));
                        failures.Add((route+" direct confirmation has no full-screen black or GrabPass surface outside the native small box",exterior==0));
                        failures.Add((route+" direct confirmation retains every front and rear native foreground pixel during motion",difference<.00001f));
                        failures.Add((route+" direct confirmation reference retains visible native foreground",BrightPixels(reference)>10));
                        Object.DestroyImmediate(actual);Object.DestroyImmediate(reference);
                    }
                    yield return null;
                }
                // Store complete motion evidence before a deliberately historical
                // missing-background flag control fails at its named oracle.
                File.WriteAllLines(Path.Combine(output,"native-confirmation-direct739.csv"),rows);
                foreach(var failure in failures)Check(failure.Value,failure.Label);failures.Clear();
                Check(!backdrop.enabled&&!blur.enabled&&small.GetComponent<Image>().enabled,"native root and Blur retire while the original small Box graphic remains enabled");
                box.IsOpen=window.IsOpen=window.IsVisible=false;if(surface==null)ConfirmationOnOwner.Tick();else surface.Tick();
                Check(backdrop.enabled&&blur.enabled&&imported.parent==expectedHome,"native full-frame graphics and exact home restore on direct confirmation close");
                surface?.Shutdown();
            }
        }
        finally
        {
            box.IsOpen=window.IsOpen=window.IsVisible=false;ConfirmationOnOwner.Tick();Singleton<UIConfirmationBoxManager>.Instance=previousManager;
            ownerRoot.SetPositionAndRotation(rootPosition,rootRotation);ownerRoot.localScale=rootScale;
            head.transform.SetPositionAndRotation(cameraPosition,cameraRotation);head.orthographic=ortho;head.nearClipPlane=near;head.farClipPlane=far;head.fieldOfView=fov;head.cullingMask=mask;head.backgroundColor=clear;
            Object.DestroyImmediate(ownerInk.gameObject);ownerPanel.HostCanvas.enabled=ownerCanvasEnabled;ownerPanel.BaseSortingOrder=oldBase;PanelLayout.WorldScale=previousScale;CanvasConversion.ResetOrder739();
            Object.DestroyImmediate(home.gameObject);Object.DestroyImmediate(sameHome.gameObject);Object.DestroyImmediate(perksWindow.gameObject);
            File.WriteAllText(Path.Combine(output,"native-confirmation-direct739-limits.txt"),"Read-only native full RectTransform/layout/image/TMP declarations, sprites, font data and direct final head pixels execute. Production full-screen-background kernel and local caller flag execute. Production distance ladder, hysteresis, follower and ordinary owner overlap execute; unrelated furniture/diagnostic calls are no-op ports. Converted host allocation/adoption is an explicit port; zero capture scheduling/resources models the observed capture-refused path. Native controllers/session callbacks, spatial character owner depth compositor and original D3D11 GrabPass fragment program are not executed. Stock editor UI shader renders native ordinary graphics; native blur uses the declared GL witness.");
        }
    }
}
