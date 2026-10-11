using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GloomhavenVR.Cards;
using GloomhavenVR.Net.TownServices;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class DiagnosticProgram
{
    public static int Checks;
    public static string Metrics = "";
    public static string NativeArtDirectory = "", OutputDirectory = "";
    private static readonly List<Object> Owned = new();
    private static void Check(bool value, string message) { Checks++; if (!value) throw new Exception(message); }
    private static T Keep<T>(T item) where T : Object { Owned.Add(item); return item; }
    private static Sprite SpriteOf(Texture2D tex, Rect rect, string name = "original")
    { Sprite sprite = Keep(Sprite.Create(tex, rect, new Vector2(.37f, .61f), 100f, 0, SpriteMeshType.FullRect, new Vector4(3,4,5,6))); sprite.name = name; return sprite; }
    private static Image ImageOf(Sprite sprite)
    {
        GameObject root = Keep(new GameObject("NativeWorldCard", typeof(RectTransform), typeof(Canvas)));
        Canvas canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
        root.transform.localScale = Vector3.one / sprite.rect.width;
        RectTransform rect = (RectTransform)root.transform; rect.sizeDelta = sprite.rect.size;
        GameObject print = new("OriginalItemPrint", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        print.transform.SetParent(root.transform, false);
        Image image = print.GetComponent<Image>(); image.sprite = sprite;
        image.rectTransform.sizeDelta = sprite.rect.size; image.rectTransform.pivot = new Vector2(.5f, .5f);
        return image;
    }
    private static Texture2D Pattern(int width, int height, string name, bool mip = false)
    {
        Texture2D tex = Keep(new Texture2D(width, height, TextureFormat.RGBA32, mip)); tex.name = name;
        var pixels = new Color32[width * height];
        for (int y=0;y<height;y++) for(int x=0;x<width;x++)
            pixels[y*width+x] = (x+y)%2==0 ? new Color32(250,250,250,255) : new Color32(8,8,8,255);
        tex.SetPixels32(pixels); tex.Apply(mip, false); tex.filterMode = FilterMode.Bilinear; return tex;
    }
    private static FieldInfo Field(string name) => typeof(CardFaceMipBake).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!;
    private static double Error(Color32[] actual, Color32[] high, int factor)
    {
        int width = (int)Math.Sqrt(actual.Length), big = width * factor;
        double error = 0;
        for(int y=0;y<width;y++) for(int x=0;x<width;x++)
        {
            double r=0,g=0,b=0;
            for(int v=0;v<factor;v++) for(int u=0;u<factor;u++)
            { Color32 p=high[(y*factor+v)*big+x*factor+u]; r+=p.r;g+=p.g;b+=p.b; }
            Color32 q=actual[y*width+x]; double count=factor*factor;
            error+=(q.r-r/count)*(q.r-r/count)+(q.g-g/count)*(q.g-g/count)+(q.b-b/count)*(q.b-b/count);
        }
        return Math.Sqrt(error / (actual.Length*3));
    }
    private static Color32[] Picture(Camera camera, int width, string picture = "")
    {
        RenderTexture target = Keep(new RenderTexture(width,width,24,RenderTextureFormat.ARGB32));
        target.Create(); camera.targetTexture=target; Canvas.ForceUpdateCanvases(); camera.Render();
        RenderTexture before=RenderTexture.active; RenderTexture.active=target;
        Texture2D read=Keep(new Texture2D(width,width,TextureFormat.RGBA32,false));
        read.ReadPixels(new Rect(0,0,width,width),0,0); read.Apply(false,false);
        RenderTexture.active=before; camera.targetTexture=null;
        if (picture.Length!=0) File.WriteAllBytes(Path.Combine(OutputDirectory,picture+".png"),read.EncodeToPNG());
        return read.GetPixels32();
    }
    public static IEnumerator Run()
    {
        Checks=0; CardsConfig.FaceMipBake.Value=true;
        // Real source loadouts used tiny untrimmed regions of one large mipless UI atlas.
        Texture2D atlas=Pattern(4096,4096,"synthetic-original-large-ui-atlas");
        Sprite source=SpriteOf(atlas,new Rect(123,267,512,497));
        long before=CardFaceMipBake.BakedVramBytes;
        Sprite? filtered=CardFaceMipBake.PresentationFor(source);
        Check(filtered!=null && filtered!=source && filtered.texture.mipmapCount>1,"small native atlas region uses mipmapped local sampling");
        Check(CardFaceMipBake.BakedVramBytes-before<2*1024*1024,"small region cannot charge or retain an entire 85 MB atlas");
        Check(filtered!.rect.size==source.rect.size && filtered.pivot==source.pivot && filtered.border==source.border && filtered.pixelsPerUnit==source.pixelsPerUnit,"filtered native geometry remains exact");
        Check(CardFaceMipBake.OriginalFor(filtered)==source,"presentation replacement retains exact original asset identity");
        Check(atlas.mipmapCount==1 && atlas.filterMode==FilterMode.Bilinear,"filtering never modifies original game-owned texture");
        Sprite duplicate=SpriteOf(atlas,source.rect,"duplicate-original-wrapper");
        long prepared=CardFaceMipBake.BakedVramBytes;
        Check(CardFaceMipBake.PresentationFor(duplicate)!.texture==filtered.texture && CardFaceMipBake.BakedVramBytes==prepared,"equivalent native region wrappers reuse one pixel allocation");
        Check(!CardFaceMipBake.RequiresColdPreparation(duplicate),"prepared region predictor and live presentation use the same cache");
        var registry=new TownServiceAssets(); registry.RegisterOriginal("native-item-art",source);
        Image observer=ImageOf(source);
        NativeImagePlayback.Apply(observer.transform, registry, new[]{0f,1f,0f,0f,1f,0f,1f,1f}, new[]{"native-item-art","native-item-art"});
        Check(CardFaceMipBake.IsBakedSprite(observer.sprite) && CardFaceMipBake.IsBakedSprite(observer.overrideSprite),"observer uses the owning native card filtering policy immediately");
        Check(registry.Key(observer.sprite)=="native-item-art" && registry.Key(observer.overrideSprite)=="native-item-art","observer filtered copies serialize as exact original assets rather than pixel payloads");
        observer.transform.parent!.gameObject.SetActive(false);
        Image image=ImageOf(source); image.overrideSprite=duplicate;
        CardFaceMipBake.Rescan(image);
        Check(CardFaceMipBake.IsBakedSprite(image.sprite) && CardFaceMipBake.IsBakedSprite(image.overrideSprite),"native base and independent override both use filtered sprites");
        CardFaceMipBake.RestoreSprites(image);
        Check(image.sprite==source && image.overrideSprite==duplicate,"pool restore returns both exact original authored sprites");
        image.overrideSprite=source; CardFaceMipBake.Rescan(image);
        Check(CardFaceMipBake.IsBakedSprite(image.sprite) && CardFaceMipBake.IsBakedSprite(image.overrideSprite),"an explicit override equal to the base is filtered independently");
        CardFaceMipBake.RestoreSprites(image);
        Check(image.sprite==source && image.overrideSprite==source,"equal explicit base and override restore original art");
        image.overrideSprite=null; CardFaceMipBake.Rescan(image);
        Sprite other=SpriteOf(Pattern(128,128,"arriving-item-art"),new Rect(0,0,128,128));
        image.sprite=other;
        Check(image.overrideSprite==other,"filtering must not invent an override which masks later native art");
        var watch=new CardArtWatch(); watch.Capture(image);
        image.overrideSprite=duplicate;
        Check(watch.Poll("native override arrival")>0 && CardFaceMipBake.IsBakedSprite(image.overrideSprite),"an override-only native art arrival is filtered before rendering");
        image.sprite=source;
        Check(watch.Poll("native base arrival with override")>0 && CardFaceMipBake.OriginalFor(image.sprite)==source && CardFaceMipBake.OriginalFor(image.overrideSprite)==duplicate,"native base arrival filters without replacing an independent active override");
        Check(watch.Poll("unchanged") == 0,"unchanged native base and override produce no sampling writes");
        CardFaceMipBake.RestoreSprites(image);
        CardsConfig.FaceMipBake.Value=false;
        Check(CardFaceMipBake.PresentationFor(source)==source,"disabled filtering leaves native original untouched");
        CardsConfig.FaceMipBake.Value=true;
        foreach(FilterMode mode in new[]{FilterMode.Point,FilterMode.Bilinear,FilterMode.Trilinear})
        {
            Texture2D original=Pattern(16,16,"native-already-mipped-"+mode,true); original.filterMode=mode;
            Sprite sprite=SpriteOf(original,new Rect(0,0,16,16));
            Check(CardFaceMipBake.PresentationFor(sprite)==sprite && original.filterMode==mode,"already-mipped native source retains its authored sampling and ownership: "+mode);
        }
        // More than 512 legitimate tiny native atlas regions must not be rejected by
        // the former count proxy while the actual VRAM resource still has headroom.
        long regionBefore=CardFaceMipBake.BakedVramBytes;
        for(int i=0;i<530;i++)
        {
            Sprite part=SpriteOf(atlas,new Rect((i%100)*8,(i/100)*8,8,8),"native-region-"+i);
            Check(CardFaceMipBake.PresentationFor(part)!=part,"legitimate native regions exceed the former 512-count proxy under the byte ceiling");
        }
        Check(CardFaceMipBake.BakedVramBytes-regionBefore<300000,"many native icon regions retain their actual small byte footprint");
        int count=(int)Field("s_spriteBakeCount").GetValue(null)!;
        Field("s_spriteBakeCount").SetValue(null,2048);
        Sprite guard=SpriteOf(atlas,new Rect(4000,4000,8,8),"runaway-guard-original");
        Check(CardFaceMipBake.PresentationFor(guard)==guard,"metadata runaway guard remains bounded at 2048 regions");
        Field("s_spriteBakeCount").SetValue(null,count);
        // The unchanged ceiling is a safe fallback, never unbounded allocation or corrupt art.
        long total=CardFaceMipBake.BakedVramBytes;
        Field("s_bakedVramBytes").SetValue(null,384L*1024*1024);
        Sprite denied=SpriteOf(Pattern(256,256,"budget-denied-new-native-original"),new Rect(0,0,256,256));
        Check(CardFaceMipBake.PresentationFor(denied)==denied,"exhausted unchanged VRAM budget retains exact original safely");
        Field("s_bakedVramBytes").SetValue(null,total);
        Check(CardFaceMipBake.PresentationFor(denied)==denied,"negative budget verdict is bounded rather than a per-frame retry storm");
        // Readback pixels and shader minification, using actual world-space uGUI and perspective.
        image.overrideSprite=null; image.sprite=source;
        image.transform.parent!.position=Vector3.zero;
        Camera camera=Keep(new GameObject("NativeCardSamplingCamera")).AddComponent<Camera>();
        camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.black;
        camera.fieldOfView=60; camera.nearClipPlane=.01f; camera.farClipPlane=30;
        double rawError=0,mipError=0; int views=0;
        foreach(int pixels in new[]{32,64,128}) foreach(float tilt in new[]{0f,45f})
        {
            image.transform.parent.rotation=Quaternion.Euler(0,tilt,0);
            camera.transform.position=new Vector3(.0017f,0,-256f/(2*Mathf.Tan(30*Mathf.Deg2Rad)*pixels));
            camera.transform.rotation=Quaternion.identity;
            image.sprite=source; Color32[] reference=Picture(camera,2048),raw=Picture(camera,256,pixels==64 && tilt==45 ? "pattern-distance-original" : "");
            image.sprite=filtered; Color32[] mip=Picture(camera,256,pixels==64 && tilt==45 ? "pattern-distance-filtered" : "");
            double a=Error(raw,reference,8),b=Error(mip,reference,8); rawError+=a;mipError+=b;views++;
            Debug.Log($"CARD MIP MINIFICATION pixels={pixels} tilt={tilt} nativeRms={a:F3} filteredRms={b:F3}");
        }
        Check(mipError<rawError*.75,"mipmapped native region reduces distance and oblique minification error against supersampled reference");
        // At one texel per output pixel the filtering changes no art, border or print resolution.
        image.transform.parent.rotation=Quaternion.identity;
        camera.transform.position=new Vector3(0,0,-1/(2*Mathf.Tan(30*Mathf.Deg2Rad)));
        image.sprite=source; Color32[] nearRaw=Picture(camera,512);
        image.sprite=filtered; Color32[] nearMip=Picture(camera,512);
        double difference=0;for(int i=0;i<nearRaw.Length;i++)difference+=Math.Abs(nearRaw[i].r-nearMip[i].r);
        Check(difference/nearRaw.Length<2,"full-resolution close native artwork is retained instead of reducing resolution");
        int nativeViews=0;
        if(NativeArtDirectory.Length!=0)
        {
            foreach(string path in Directory.GetFiles(NativeArtDirectory,"*.png"))
            {
                Texture2D native=Keep(new Texture2D(2,2,TextureFormat.RGBA32,false));
                Check(ImageConversion.LoadImage(native,File.ReadAllBytes(path),false),"actual native item pixels decode");
                native.name=Path.GetFileNameWithoutExtension(path); native.filterMode=FilterMode.Bilinear;
                Sprite original=SpriteOf(native,new Rect(0,0,native.width,native.height));
                Sprite replacement=CardFaceMipBake.PresentationFor(original)!;
                Check(replacement!=original && replacement.texture.mipmapCount>1,"actual original item art receives complete mip chain");
                double itemRaw=0,itemMip=0;
                foreach(int pixels in new[]{32,64,128}) foreach(float tilt in new[]{0f,45f})
                {
                    image.rectTransform.sizeDelta=original.rect.size;
                    image.transform.parent.rotation=Quaternion.Euler(0,tilt,0);
                    camera.transform.position=new Vector3(.0017f,0,-256f/(2*Mathf.Tan(30*Mathf.Deg2Rad)*pixels));
                    image.sprite=original; Color32[] reference=Picture(camera,2048),raw=Picture(camera,256,pixels==64 && tilt==45 ? native.name+"-distance-original" : "");
                    image.sprite=replacement; Color32[] mip=Picture(camera,256,pixels==64 && tilt==45 ? native.name+"-distance-filtered" : "");
                    double a=Error(raw,reference,8),b=Error(mip,reference,8);itemRaw+=a;itemMip+=b;nativeViews++;
                    Debug.Log($"CARD MIP ORIGINAL ART {native.name} pixels={pixels} tilt={tilt} nativeRms={a:F3} filteredRms={b:F3}");
                }
                Check(itemMip<itemRaw,"actual native original item artwork reduces aggregate distance/oblique sampling error");
                image.transform.parent.rotation=Quaternion.identity;
                camera.transform.position=new Vector3(0,0,-1/(2*Mathf.Tan(30*Mathf.Deg2Rad)));
                image.sprite=original; Color32[] originalNear=Picture(camera,512,native.name+"-near-original");
                image.sprite=replacement; Color32[] filteredNear=Picture(camera,512,native.name+"-near-filtered");
                double nativeDelta=0;
                for(int i=0;i<originalNear.Length;i++)
                    nativeDelta+=Math.Abs(originalNear[i].r-filteredNear[i].r)+Math.Abs(originalNear[i].g-filteredNear[i].g)+Math.Abs(originalNear[i].b-filteredNear[i].b);
                Check(nativeDelta/(originalNear.Length*3)<2,"actual original item close artwork and sRGB color remain unchanged");
                Debug.Log($"CARD MIP ORIGINAL NEAR {native.name} delta={nativeDelta/(originalNear.Length*3):F3}; distanceRms={itemRaw/6:F3}->{itemMip/6:F3}");
            }
        }
        Metrics=$"native item views={nativeViews}; {views} minified perspective views; aggregate RMS {rawError/views:F3} -> {mipError/views:F3}; near difference {difference/nearRaw.Length:F3}; VRAM={CardFaceMipBake.BakedVramBytes}";
        yield return null;
    }
    public static void Cleanup() { foreach(Object item in Owned) if(item!=null) Object.DestroyImmediate(item); Owned.Clear(); }
}
