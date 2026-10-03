using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using TownServiceAssets = GloomhavenVR.Net.TownServices.TownServiceAssets;

// Only the native serialized catalogue container is a boundary. Its slot names and pixels
// are exported from UIInfoTools level1:11386, with source hashes in native/provenance.json.
public sealed class UIInfoTools
{
    public struct EffectInfo { public Sprite Icon, BigIcon, TempleIcon; }
    public EffectInfo Poisoned, Wounded;
    public Sprite Target = null!;
}
public static partial class MirrorProgram
{
    private static string PeerRoot => Directory.GetParent(_output)!.FullName;
    private static Texture2D OriginalPng(string file)
    {
        var texture = new Texture2D(2,2,TextureFormat.RGBA32,false);
        Check(texture.LoadImage(File.ReadAllBytes(Path.Combine(PeerRoot,"native",file))),"original native pixels decode");
        Assets.Add(texture); return texture;
    }
    private static UIInfoTools OriginalCatalogue(bool observer)
    {
        string[] names = {"Poisoned","Wounded","Target"};
        var atlas = new Texture2D(512,256,TextureFormat.RGBA32,false)
        { name = observer ? "sactx-0-4096x4096-DXT5|BC3-BattleOverlayCanvas-native-observer" : "sactx-0-4096x4096-DXT5|BC1-BattleOverlayCanvas-native-owner" };
        var clear=new Color[512*256];atlas.SetPixels(clear);Assets.Add(atlas);
        var sprites=new Sprite[3];
        for(int i=0;i<names.Length;i++)
        {
            Texture2D original=OriginalPng(names[i]+".png");int x=(observer?2-i:i)*150;
            atlas.SetPixels(x,0,original.width,original.height,original.GetPixels());
            atlas.Apply();
            sprites[i]=Sprite.Create(atlas,new Rect(x,0,original.width,original.height),Vector2.one*.5f,100f,0,SpriteMeshType.FullRect);
            sprites[i].name=names[i];Assets.Add(sprites[i]);
        }
        atlas.Apply();
        for(int i=0;i<sprites.Length;i++)
        {
            Texture2D original=OriginalPng(names[i]+".png"); Rect rect=sprites[i].rect;
            Color[] packed=atlas.GetPixels((int)rect.x,(int)rect.y,(int)rect.width,(int)rect.height),native=original.GetPixels();
            Check(packed.Length==native.Length,"native packed sprite keeps the original extent");
            for(int pixel=0;pixel<native.Length;pixel++)Check(packed[pixel]==native[pixel],"independent packed original retains exact native sprite texels");
        }
        return new UIInfoTools {Poisoned=new UIInfoTools.EffectInfo {Icon=sprites[0]},Wounded=new UIInfoTools.EffectInfo {Icon=sprites[1]},Target=sprites[2]};
    }
    private static Transform NativeSurface(UIInfoTools catalog,Texture2D mask)
    {
        Transform root=Go("Native condition surface").transform;root.localScale=Vector3.one*.01f;
        ((RectTransform)root).sizeDelta=new Vector2(240,130);
        Canvas canvas=root.gameObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.worldCamera=_camera;
        Sprite[] icons={catalog.Poisoned.Icon,catalog.Wounded.Icon,catalog.Target};
        for(int i=0;i<3;i++)
        {
            RectTransform rect=Rect("Native icon "+i,root,new Vector2((i-1)*70,20),Vector2.one*48);
            var glyph=rect.gameObject.AddComponent<Image>();glyph.sprite=icons[i];
            // Own the fixture UI material explicitly; the pixels and serialized catalogue
            // fields remain the exported native originals, not fabricated artwork.
            var nativeMaterial=new Material(Shader.Find("UI/Default"));Assets.Add(nativeMaterial);glyph.material=nativeMaterial;
        }
        RectTransform effect=Rect("Native effect",root,new Vector2(0,-40),new Vector2(220,35));
        var image=effect.gameObject.AddComponent<RawImage>();image.texture=mask;
        var material=new Material(Shader.Find("UI/Default"));material.SetTexture("_MainTex",mask);Assets.Add(material);image.material=material;
        return root;
    }
    private static TownServiceFrame NativeFrame(Transform root,TownServiceAssets assets)
    {
        using var binding=new TownServiceBinding(root);
        return new TownServiceFrame {Service=3,Session=612,Sequence=1,Module=10,Template=1,Structure=binding.Structure,
            Visible=true,Pose=new[]{0f,0f,0f,0f,0f,0f,1f,1f,1f,1f},Nodes=binding.Read(assets)};
    }
    private static IEnumerator NativePeer(string suite)
    {
        var cameraObject=Go("Peer camera");_camera=cameraObject.AddComponent<Camera>();_camera.enabled=false;
        _camera.orthographic=true;_camera.nearClipPlane=.01f;_camera.farClipPlane=100;_camera.backgroundColor=new Color(.025f,.03f,.04f,1);_camera.clearFlags=CameraClearFlags.SolidColor;
        GloomhavenVR.Rig.VRRigDriver.HeadCamera=_camera;
        bool observer=suite.EndsWith("observer",StringComparison.Ordinal);
        UIInfoTools catalog=OriginalCatalogue(observer);Texture2D mask=OriginalPng("effect-mask.png");mask.name="T_rect_frame_mask_card_wide";
        var registry=new TownServiceAssets();TownServiceTemplateAssets.RegisterSpriteCatalog(registry,catalog);
        Sprite baked=Sprite.Create(catalog.Target.texture,catalog.Target.rect,Vector2.one*.5f,100f,0,SpriteMeshType.FullRect);Assets.Add(baked);
        GloomhavenVR.Cards.CardFaceMipBake.Originals[baked]=catalog.Target;
        Check(registry.Key(baked)==registry.Key(catalog.Target),"baked sprite resolves the verified original before cached identity lookup");
        // The very same immutable original occurs in multiple lazy cards. Owner/observer
        // borrow them in opposite orders; different instance IDs are never transmitted.
        Transform root=NativeSurface(catalog,mask);
        TownServiceTemplateAssets.Register(registry,observer?"card.324":"card.322",root);
        TownServiceTemplateAssets.Register(registry,observer?"card.322":"card.324",root);
        yield return null;Canvas.ForceUpdateCanvases();
        Render(root,9,"native-initial-upload");
        yield return null;Canvas.ForceUpdateCanvases();
        NativeDump(root, "before");
        if(!observer)
        {
            File.WriteAllBytes(Path.Combine(PeerRoot,"native-owner.gvr"),TownServiceCodec.Write(NativeFrame(root,registry)));
            Render(root,9,"native-owner");
            // Model faces omit stale pooled artwork but must register original FX textures.
            // A separate registry is a separate capture lifetime, as after Mirror.Reset.
            TownServiceMaterial.Reset();
            var fxRegistry=new TownServiceAssets();TownServiceTemplateAssets.RegisterSpriteCatalog(fxRegistry,catalog);
            TownServiceTemplateAssets.Register(fxRegistry,"face.611",root);
            File.WriteAllBytes(Path.Combine(PeerRoot,"native-model-fx.gvr"),TownServiceCodec.Write(NativeFrame(root,fxRegistry)));
            yield break;
        }
        byte[] data=File.ReadAllBytes(Path.Combine(PeerRoot,"native-owner.gvr"));
        Check(TownServiceCodec.TryRead(data,data.Length,out TownServiceFrame? decoded),"independent original owner frame decodes");
        GameObject copy=Object.Instantiate(root.gameObject);copy.SetActive(false);Assets.Add(copy);
        TownServiceNeutralize.Apply(copy);Transform nativeSource=root;root=copy.transform;
        root.gameObject.SetActive(true); nativeSource.gameObject.SetActive(false);
        using var visibleBinding=new TownServiceBinding(root);visibleBinding.Validate(decoded!,registry);visibleBinding.Apply(decoded!,registry);
        Check(root.Find("Native icon 0").GetComponent<Image>().sprite==catalog.Poisoned.Icon,"original owner Poison icon resolves through observer catalogue despite atlas and UV changes");
        Check(root.Find("Native icon 1").GetComponent<Image>().sprite==catalog.Wounded.Icon,"original Wound sprite reference is preserved");
        Check(root.Find("Native icon 2").GetComponent<Image>().sprite==catalog.Target,"original Target sprite reference is preserved");
        Check(root.Find("Native effect").GetComponent<RawImage>().texture==mask,"later verified card provenance resolves the actual native effect mask");
        yield return null;Render(root,9,"native-observer"); NativeDump(root,"after");
        using(var ownerPng=ReadRender(Path.Combine(PeerRoot,"owner-evidence","native-owner.png")))
        using(var nativeObserverPng=ReadRender(Path.Combine(_output,"native-initial-upload.png")))
        using(var observerPng=ReadRender(Path.Combine(_output,"native-observer.png")))
        {
            Color32[] first=ownerPng.Value.GetPixels32(),original=nativeObserverPng.Value.GetPixels32(),second=observerPng.Value.GetPixels32();
            int priorDifferent=0,changed=0,ink=0;
            for(int i=0;i<first.Length;i++)
            {
                if(first[i].r>35||first[i].g>35||first[i].b>35)ink++;
                if(!first[i].Equals(original[i]))priorDifferent++;
                if(!original[i].Equals(second[i]))changed++;
            }
            Check(ink>500,"actual native condition/effect artwork has visible rendered pixels");
            Check(changed==0,"separate process playback exactly preserves the observer original native icon and effect pixels");
            // Independently packed native atlases rasterize their bilinear UV edges differently
            // even before playback. Preserve both original pictures and measure that boundary;
            // never permit playback to add a pixel difference on the observer's original.
            File.WriteAllText(Path.Combine(_output,"pixel-counts.txt"),"native ink="+ink+"; independent packed originals="+priorDifferent+"; playback changes="+changed+"\n");
        }
        Transform fxRoot=NativeSurface(catalog,mask);fxRoot.gameObject.SetActive(false);
        var fxAssets=new TownServiceAssets();TownServiceTemplateAssets.RegisterSpriteCatalog(fxAssets,catalog);
        TownServiceTemplateAssets.Register(fxAssets,"face.611",fxRoot);
        data=File.ReadAllBytes(Path.Combine(PeerRoot,"native-model-fx.gvr"));
        Check(TownServiceCodec.TryRead(data,data.Length,out decoded),"independent original model FX frame decodes");
        using(var binding=new TownServiceBinding(fxRoot)){binding.Validate(decoded!,fxAssets);binding.Apply(decoded!,fxAssets);}
        bool refused=false;try {fxAssets.RegisterOriginal("native-town|addressable-sprite|same",catalog.Poisoned.Icon);fxAssets.RegisterOriginal("native-town|addressable-sprite|same",catalog.Wounded.Icon);}catch(InvalidDataException){refused=true;}
        Check(refused,"different native assets cannot share an explicit original identity");
        root.gameObject.SetActive(false);
        IEnumerator geometry=NativeBackingMotion();while(geometry.MoveNext())yield return geometry.Current;
        geometry=NativeRootBounds(catalog,mask);while(geometry.MoveNext())yield return geometry.Current;
    }
    private static void NativeDump(Transform root,string stage)
    {
        foreach(var glyph in root.GetComponentsInChildren<Image>())
        {
            var vertexHelper=new VertexHelper();
            typeof(Image).GetMethod("OnPopulateMesh",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance,null,new[]{typeof(VertexHelper)},null)!.Invoke(glyph,new object[]{vertexHelper});
            var mesh=new Mesh(); vertexHelper.FillMesh(mesh); vertexHelper.Dispose();
            File.AppendAllText(Path.Combine(_output,"sprite-dump.txt"),stage+" "+glyph.name+" sprite="+glyph.sprite.name+" override="+glyph.overrideSprite.name+" rect="+glyph.sprite.rect+" material="+glyph.material.GetInstanceID()+" UV="+string.Join(";",mesh.uv)+"\n");
            Object.DestroyImmediate(mesh);
        }
    }
    private sealed class TextureLease : IDisposable
    {internal readonly Texture2D Value;internal TextureLease(Texture2D value){Value=value;}public void Dispose(){Object.DestroyImmediate(Value);}}
    private static TextureLease ReadRender(string path){var texture=new Texture2D(2,2);Check(texture.LoadImage(File.ReadAllBytes(path)),"native render comparison image decodes");return new TextureLease(texture);}
    private static IEnumerator NativeRootBounds(UIInfoTools catalog,Texture2D mask)
    {
        TownServiceMirror.Shutdown();
        Transform shared=Go("Original native root frame").transform,viewer=Go("Observer native root frame").transform;viewer.position=Vector3.right*12;
        var outer=(RectTransform)Go("Native enclosing canvas",shared).transform;outer.sizeDelta=new Vector2(600,400);outer.localScale=Vector3.one*.01f;
        outer.gameObject.AddComponent<Canvas>().renderMode=RenderMode.WorldSpace;
        Transform source=NativeSurface(catalog,mask);Object.DestroyImmediate(source.GetComponent<Canvas>());
        source.SetParent(outer,false);source.localScale=Vector3.one;
        // This case isolates geometry with the real exported native frame mask. Packed
        // native icon identity/pixels are tested separately above across two processes.
        // A root graphic makes the extent itself visible, rather than merely checking
        // child icons whose fixed sizes would also render under a stale frozen root.
        foreach(Transform child in source)child.gameObject.SetActive(false);
        var boundsInk=source.gameObject.AddComponent<RawImage>();boundsInk.texture=mask;
        var boundsMaterial=new Material(Shader.Find("UI/Default"));boundsMaterial.mainTexture=mask;Assets.Add(boundsMaterial);boundsInk.material=boundsMaterial;
        var stretched=Rect("Native stretched mask",source,Vector2.zero,new Vector2(170,55));
        stretched.anchorMin=new Vector2(.15f,.2f);stretched.anchorMax=new Vector2(.85f,.8f);stretched.pivot=new Vector2(.25f,.75f);
        stretched.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,170);stretched.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,55);
        var childInk=stretched.gameObject.AddComponent<RawImage>();childInk.texture=mask;childInk.material=boundsMaterial;
        const string address="enchant.holder|";
        TownServiceMirror.RegisterTemplate(3,1,source,address:address);
        // Freeze was 240x130. The live native root has an independently resized,
        // stretched rect and pivot, all under the unchanged real enclosing canvas.
        var rect=(RectTransform)source;rect.anchorMin=new Vector2(.1f,.2f);rect.anchorMax=new Vector2(.9f,.8f);rect.pivot=new Vector2(.42f,.47f);
        rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,360);
        rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,180);
        TownServiceMirror.BeginSession(3,615,shared,source);TownServiceMirror.RegisterModule(10,1,source,address:address);TownServiceMirror.SharedFrameForRemote=_=>viewer;
        yield return null;FastCapture initial=CaptureFast();Receive(1,initial.Artwork);DeliverMotion(1,initial);
        IEnumerator settle=FastSettle(viewer,.13f);while(settle.MoveNext())yield return settle.Current;
        TownServiceBinding copy=Remote(1,10)!;Check(copy!=null,"resized original framed native root reconstructs");
        var observed=(RectTransform)copy.Root;
        Check(Vector2.Distance(observed.rect.size,rect.rect.size)<.001f&&observed.pivot==rect.pivot&&observed.anchorMin==rect.anchorMin&&observed.anchorMax==rect.anchorMax,
            "baseline retains the actual native root rect instead of frozen prefab dimensions");
        CheckRootChildren(source,copy.Root,"baseline");
        ComparePixels(source,copy.Root,"native-root-baseline");
        rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,470);
        rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,230);rect.pivot=new Vector2(.37f,.51f);
        yield return null;FastCapture resized=CaptureFast();
        Check(!resized.Artwork.Exists(bytes=>TownServiceCodec.TryRead(bytes,bytes.Length,out TownServiceFrame? frame)&&frame!.Module==10),
            "native root resizing uses numbers without resending validated original artwork");
        DeliverMotion(1,resized);settle=FastSettle(viewer,.14f);while(settle.MoveNext())yield return settle.Current;
        Check(Vector2.Distance(observed.rect.size,rect.rect.size)<.001f&&observed.pivot==rect.pivot,
            "numeric native root resize preserves the complete visible extent and pivot");
        CheckRootChildren(source,copy.Root,"fast");
        ComparePixels(source,copy.Root,"native-root-fast");
        File.WriteAllText(Path.Combine(_output,"root-bounds.txt"),"frozen=240x130; baseline=360x180; fast="+observed.rect.size+"; pivot="+observed.pivot+"\n");
    }
    private static void CheckRootChildren(Transform original,Transform observed,string stage)
    {
        foreach(RectTransform rect in original.GetComponentsInChildren<RectTransform>(true))
        {
            Transform? mapped=rect==original?observed:observed.Find(rect.name);Check(mapped is RectTransform,"native child geometry maps: "+stage);
            var copy=(RectTransform)mapped!;
            Check(Vector2.Distance(copy.rect.size,rect.rect.size)<.001f&&copy.pivot==rect.pivot&&copy.anchorMin==rect.anchorMin&&copy.anchorMax==rect.anchorMax,
                "native descendant actual extent preserves stretch anchors: "+stage);
        }
    }
    private static Color32[] RenderBody(Transform root,string name)
    {
        foreach(GameObject fixture in Objects)if(fixture!=null)Layer(fixture.transform,30);
        Layer(root,8); _camera.cullingMask=1<<8;
        _camera.transform.SetPositionAndRotation(root.position-root.forward*2,root.rotation);
        _camera.orthographicSize=.065f*root.lossyScale.y;
        var rt=new RenderTexture(512,384,24,RenderTextureFormat.ARGB32){antiAliasing=1};
        var image=new Texture2D(512,384,TextureFormat.RGBA32,false);
        try
        {
            _camera.targetTexture=rt;_camera.Render();RenderTexture.active=rt;
            image.ReadPixels(new Rect(0,0,512,384),0,0);image.Apply();
            File.WriteAllBytes(Path.Combine(_output,name+".png"),image.EncodeToPNG());return image.GetPixels32();
        }
        finally{RenderTexture.active=null;_camera.targetTexture=null;Object.DestroyImmediate(rt);Object.DestroyImmediate(image);}
    }
    private static IEnumerator NativeBackingMotion()
    {
        TownServiceMirror.Shutdown();
        Transform shared=Go("Original body frame").transform,viewer=Go("Observer body frame").transform;viewer.position=Vector3.right*12;
        GameObject body=Go("Native procedural item backing",shared);
        Mesh mesh=OriginalCardBody.Create(.0635f,.073f);Assets.Add(mesh);
        body.AddComponent<MeshFilter>().sharedMesh=mesh;body.AddComponent<MeshRenderer>();
        Check(mesh.vertexCount>100&&mesh.subMeshCount==2,"real production rounded item backing geometry is used");
        body.transform.localPosition=new Vector3(.02f,.04f,.01f);body.transform.localRotation=Quaternion.Euler(0,180,9);
        body.transform.localScale=new Vector3(.67f,.83f,.91f);
        // Shader portability only: the original bundle's geometry is unchanged. The GL
        // editor cannot execute Windows-only bundle shader variants; use engine-native UI
        // texture sampling for both pictures, without replacing production geometry logic.
        foreach(MeshRenderer renderer in body.GetComponentsInChildren<MeshRenderer>(true))
        {Material material=new Material(Shader.Find("UI/Default"));material.mainTexture=Texture2D.whiteTexture;material.color=new Color(.7f,.6f,.4f);Assets.Add(material);renderer.sharedMaterials=new[]{material,material};}
        yield return null;
        const string address="inspectionbody.3d820c4a.3d820c4a.p|";
        TownServiceMirror.RegisterTemplate(1,1,body.transform,address:address);TownServiceMirror.BeginSession(1,614,shared,body.transform);
        TownServiceMirror.RegisterModule(10,1,body.transform,address:address);TownServiceMirror.SharedFrameForRemote=_=>viewer;
        FastCapture first=CaptureFast();Receive(1,first.Artwork);DeliverMotion(1,first);
        IEnumerator settle=FastSettle(viewer,.13f);while(settle.MoveNext())yield return settle.Current;
        TownServiceBinding copy=Remote(1,10) ?? throw new InvalidOperationException("Missing original body module");Check(copy!=null,"real original body module reconstructs");
        body.transform.localPosition+=new Vector3(.015f,.03f,-.02f);body.transform.localRotation=Quaternion.Euler(4,166,17);body.transform.localScale=new Vector3(.78f,.88f,.92f);
        yield return null;FastCapture next=CaptureFast();DeliverMotion(1,next);
        settle=FastSettle(viewer,.14f);while(settle.MoveNext())yield return settle.Current;
        Check(copy.Root.localPosition==Vector3.zero&&Quaternion.Angle(copy.Root.localRotation,Quaternion.identity)<.001f&&copy.Root.localScale==Vector3.one,
            "detached root normalized after native numeric transform");
        Check(Vector3.Distance(copy.Root.position,viewer.TransformPoint(shared.InverseTransformPoint(body.transform.position)))<.0001f,"original backing world position survives complete plus fast path");
        Check(Quaternion.Angle(copy.Root.rotation,body.transform.rotation)<.001f,"original backing rotation is applied once");
        Check(Vector3.Distance(copy.Root.lossyScale,body.transform.lossyScale)<.0001f,"original backing crop scale is applied once");
        Check(copy.Root.GetComponentInChildren<MeshFilter>(true).sharedMesh==body.GetComponentInChildren<MeshFilter>(true).sharedMesh,"fast observer keeps real production ItemChip fallback mesh");
        Color32[] sourcePixels=RenderBody(body.transform,"body-owner"),observerPixels=RenderBody(copy.Root,"body-observer");
        int visible=0,different=0;
        for(int i=0;i<sourcePixels.Length;i++)
        {if(sourcePixels[i].r>35||sourcePixels[i].g>35||sourcePixels[i].b>35)visible++;if(!sourcePixels[i].Equals(observerPixels[i]))different++;}
        Check(visible>5000,"actual rounded production item backing has visible body pixels");
        Check(different<200,"detached numeric body preserves the actual original backing picture");
        File.WriteAllText(Path.Combine(_output,"body-pixels.txt"),"native rounded mesh vertices="+mesh.vertexCount+" triangles="+mesh.triangles.Length/3+"; visible="+visible+"; changed="+different+"\n");
        File.WriteAllText(Path.Combine(_output,"body-geometry.txt"),"source="+body.transform.localToWorldMatrix+"\nobserver="+copy.Root.localToWorldMatrix+"\n");

    }
}
