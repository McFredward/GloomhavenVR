extern alias native;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.Surfaces;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static partial class LevelUpNativeProgram
{
    [Serializable] private sealed class NativeNode739
    {public string name="";public bool active=true;public NativeComponent739[] components=Array.Empty<NativeComponent739>();public NativeNode739[] children=Array.Empty<NativeNode739>();}
    [Serializable] private sealed class NativeComponent739
    {public string kind="",json="",sprite="",shader="",font="";public float[] border=Array.Empty<float>(),pivot=Array.Empty<float>();public float ppu=100;}
    [Serializable] private sealed class NativeRect739
    {public Vector3 m_LocalPosition=default,m_LocalScale=Vector3.one;public Quaternion m_LocalRotation=Quaternion.identity;public Vector2 m_AnchorMin=default,m_AnchorMax=default,m_AnchoredPosition=default,m_SizeDelta=default,m_Pivot=default;}
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
    private static RectTransform ImportNode739(NativeNode739 node,Transform parent,Dictionary<string,TMP_FontAsset> fonts)
    {
        var rect=Rect(node.name,parent,0,0);rect.gameObject.SetActive(false);
        foreach(var part in node.components)
        {
            if(part.kind=="RectTransform")
            {
                var data=JsonUtility.FromJson<NativeRect739>(part.json);rect.anchorMin=data.m_AnchorMin;rect.anchorMax=data.m_AnchorMax;
                rect.pivot=data.m_Pivot;rect.sizeDelta=data.m_SizeDelta;rect.anchoredPosition=data.m_AnchoredPosition;
                rect.localPosition=new Vector3(rect.localPosition.x,rect.localPosition.y,data.m_LocalPosition.z);
                rect.localRotation=data.m_LocalRotation;rect.localScale=data.m_LocalScale;
            }
            else if(part.kind=="Image")
            {
                var image=rect.gameObject.AddComponent<Image>();JsonUtility.FromJsonOverwrite(part.json,image);
                if(part.sprite.Length>0)
                {
                    var texture=LoadTexture(part.sprite);image.sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),
                        new Vector2(part.pivot[0],part.pivot[1]),part.ppu,0,SpriteMeshType.FullRect,
                        new Vector4(part.border[0],part.border[1],part.border[2],part.border[3]));
                }
                if(part.shader=="Custom/SimpleGrabPassBlur")image.material=new Material(Resources.Load<Shader>("ConfirmationGrab738"));
                else Check(part.shader=="UI/Default","native confirmation foreground retains the original stock UI material family");
            }
            else if(part.kind=="TextMeshProUGUI")
            {
                var text=rect.gameObject.AddComponent<TextMeshProUGUI>();JsonUtility.FromJsonOverwrite(part.json,text);
                if(!fonts.TryGetValue(part.font,out TMP_FontAsset font)){font=Font739(part.font);fonts.Add(part.font,font);}
                text.font=font;text.fontSharedMaterial=font.material;
            }
            else
            {
                Type? type=part.kind=="VerticalLayoutGroup"?typeof(VerticalLayoutGroup):part.kind=="HorizontalLayoutGroup"?typeof(HorizontalLayoutGroup)
                    :part.kind=="ContentSizeFitter"?typeof(ContentSizeFitter):part.kind=="LayoutElement"?typeof(LayoutElement):null;
                if(type!=null)JsonUtility.FromJsonOverwrite(part.json,rect.gameObject.AddComponent(type));
            }
        }
        rect.name=node.name;
        foreach(var child in node.children)ImportNode739(child,rect,fonts);
        rect.gameObject.SetActive(node.active);return rect;
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
        var ownerPanel=CanvasConversion.ActivePanels.Find(p=>ReferenceEquals(p.Target,ownerRoot))!;int oldBase=ownerPanel.BaseSortingOrder;ownerPanel.BaseSortingOrder=1000;
        var ownerInk=Image(ownerRoot,"original owner photograph overlap",1920,1080,new Color(.42f,.11f,.07f,1));ownerInk.transform.SetAsFirstSibling();
        var home=Rect("native confirmation import home",null,1920,1080);Object.DontDestroyOnLoad(home.gameObject);
        var imported=ImportNode739(JsonUtility.FromJson<NativeNode739>(File.ReadAllText(Path.Combine(_inputs,"native-confirmation-tree.json"))),home,new Dictionary<string,TMP_FontAsset>());
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
            foreach(string route in new[]{"owned","standalone"})
            {
                box.IsOpen=window.IsOpen=window.IsVisible=true;DialogSurface? surface=null;
                if(route=="owned"){Record(manager,owner);ConfirmationOnOwner.Tick();}
                else {var scope=ConfirmationOnOwner.CurrentScope;ConfirmationOnOwner.CurrentScope=null;ConfirmationOnOwner.Record(manager);ConfirmationOnOwner.CurrentScope=scope;surface=new DialogSurface();surface.Tick();}
                var panel=CanvasConversion.ActivePanels.Find(p=>ReferenceEquals(p.Target,imported))!;
                if(route=="standalone"){panel.HostRect.SetPositionAndRotation(ownerRoot.position,ownerRoot.rotation);panel.HostRect.localScale=ownerRoot.lossyScale;}
                CanvasConversion.ResetOrder739();
                // Captures are deliberately absent: fresh738 logs report negative
                // remaining session capacity and this exact direct rendering route.
                Check(small.rect.width>300&&small.rect.height>60&&small.rect.height<1080,"original native layout resolves a small dialog rather than the full-screen frame");
                for(int frame=0;frame<10;frame++)
                {
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
                Check(backdrop.enabled&&blur.enabled&&imported.parent==home,"native full-frame graphics and exact home restore on direct confirmation close");
                surface?.Shutdown();
            }
        }
        finally
        {
            box.IsOpen=window.IsOpen=window.IsVisible=false;ConfirmationOnOwner.Tick();Singleton<UIConfirmationBoxManager>.Instance=previousManager;
            ownerRoot.SetPositionAndRotation(rootPosition,rootRotation);ownerRoot.localScale=rootScale;
            head.transform.SetPositionAndRotation(cameraPosition,cameraRotation);head.orthographic=ortho;head.nearClipPlane=near;head.farClipPlane=far;head.fieldOfView=fov;head.cullingMask=mask;head.backgroundColor=clear;
            Object.DestroyImmediate(ownerInk.gameObject);ownerPanel.BaseSortingOrder=oldBase;PanelLayout.WorldScale=previousScale;CanvasConversion.ResetOrder739();
            Object.DestroyImmediate(home.gameObject);
            File.WriteAllText(Path.Combine(output,"native-confirmation-direct739-limits.txt"),"Read-only native full RectTransform/layout/image/TMP declarations, sprites, font data and direct final head pixels execute. Production full-screen-background kernel and local caller flag execute. Production distance ladder, hysteresis, follower and ordinary owner overlap execute; unrelated furniture/diagnostic calls are no-op ports. Converted host allocation/adoption is an explicit port; zero capture scheduling/resources models the observed capture-refused path. Native controllers/session callbacks, spatial character owner depth compositor and original D3D11 GrabPass fragment program are not executed. Stock editor UI shader renders native ordinary graphics; native blur uses the declared GL witness.");
        }
    }
}
