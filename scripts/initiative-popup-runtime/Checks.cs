using System;
using System.IO;
using System.Reflection;
using GloomhavenVR.WorldUI;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class InitiativePopupChecks
{
    private static int _checks;
    [Serializable] public sealed class Float { public string name=""; public float value=0; }
    [Serializable] public sealed class Tint { public string name=""; public Color value=Color.white; }
    [Serializable] public sealed class Saved { public string[] keywords=Array.Empty<string>(); public int renderQueue=0; public Float[] floats=Array.Empty<Float>(); public Tint[] colors=Array.Empty<Tint>(); }
    private static void Check(bool condition,string reason) { _checks++; if(!condition)throw new Exception(reason); }
    private static RectTransform Rect(string name,Transform parent,Vector2 size)
    {
        var go=new GameObject(name,typeof(RectTransform));var rect=(RectTransform)go.transform;
        rect.SetParent(parent,false);rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);rect.sizeDelta=size;return rect;
    }
    private static Canvas Canvas(Transform parent,string name)
    {
        var rect=Rect(name,parent,Vector2.one);var canvas=rect.gameObject.AddComponent<Canvas>();
        canvas.renderMode=RenderMode.WorldSpace;return canvas;
    }
    private static TMP_FontAsset Font(string inputs)
    {
        var instance=typeof(TMP_Settings).GetField("s_Instance",BindingFlags.Static|BindingFlags.NonPublic)!;
        if(instance.GetValue(null)==null)instance.SetValue(null,ScriptableObject.CreateInstance<TMP_Settings>());
        var font=ScriptableObject.CreateInstance<TMP_FontAsset>();JsonUtility.FromJsonOverwrite(File.ReadAllText(Path.Combine(inputs,"font.json")),font);
        var atlas=new Texture2D(2,2,TextureFormat.RGBA32,false);atlas.LoadImage(File.ReadAllBytes(Path.Combine(inputs,"font-atlas.png")));
        font.atlasTextures=new[]{atlas};font.atlasPopulationMode=AtlasPopulationMode.Static;
        var material=new Material(Resources.Load<Shader>("OriginalShaders/NativeSDF"));material.mainTexture=atlas;
        var properties=JsonUtility.FromJson<Saved>(File.ReadAllText(Path.Combine(inputs,"font-material.json")));
        material.shaderKeywords=properties.keywords;material.renderQueue=properties.renderQueue;
        foreach(var value in properties.floats)material.SetFloat(value.name,value.value);
        foreach(var value in properties.colors)material.SetColor(value.name,value.value);
        font.material=material;font.ReadFontAssetDefinition();return font;
    }
    private static Texture2D Capture(Camera eye,string path)
    {
        UnityEngine.Canvas.ForceUpdateCanvases();eye.Render();var previous=RenderTexture.active;RenderTexture.active=eye.targetTexture;
        var result=new Texture2D(eye.targetTexture.width,eye.targetTexture.height,TextureFormat.RGBA32,false);
        result.ReadPixels(new Rect(0,0,result.width,result.height),0,0);result.Apply();RenderTexture.active=previous;
        File.WriteAllBytes(path,result.EncodeToPNG());return result;
    }
    private static Color Sample(Texture2D image,Camera eye,Vector3 world)
    {
        Vector3 pixel=eye.WorldToScreenPoint(world);
        return image.GetPixel(Mathf.Clamp(Mathf.RoundToInt(pixel.x),0,image.width-1),Mathf.Clamp(Mathf.RoundToInt(pixel.y),0,image.height-1));
    }
    private static int WhitePixels(Texture2D image,Camera eye,RectTransform rect)
    {
        var corners=new Vector3[4];rect.GetWorldCorners(corners);
        Vector3 first=eye.WorldToScreenPoint(corners[0]),last=eye.WorldToScreenPoint(corners[2]);int count=0;
        for(int y=Mathf.Clamp(Mathf.FloorToInt(first.y),0,image.height-1);y<=Mathf.Clamp(Mathf.CeilToInt(last.y),0,image.height-1);y++)
            for(int x=Mathf.Clamp(Mathf.FloorToInt(first.x),0,image.width-1);x<=Mathf.Clamp(Mathf.CeilToInt(last.x),0,image.width-1);x++)
            {Color p=image.GetPixel(x,y);if(p.r>.5f&&p.g>.5f&&p.b>.5f)count++;}
        return count;
    }
    public static string Run(string output,string inputs)
    {
        _checks=0;string label=typeof(InitiativePopupChecks).Assembly.GetName().Name!;
        var root=new GameObject("owner board").transform;
        var eye=new GameObject("head",typeof(Camera)).GetComponent<Camera>();eye.enabled=false;
        eye.orthographic=true;eye.orthographicSize=.65f;eye.aspect=4f/3f;eye.nearClipPlane=.01f;eye.farClipPlane=10;
        eye.clearFlags=CameraClearFlags.SolidColor;eye.backgroundColor=Color.black;
        var target=new RenderTexture(960,720,24);target.Create();eye.targetTexture=target;
        var board=GameObject.CreatePrimitive(PrimitiveType.Cube);board.name="opaque own board and raised rim";board.transform.SetParent(root,false);
        board.transform.localPosition=new Vector3(0,-.1f,0);board.transform.localScale=new Vector3(1,.75f,.12f);
        var boardMaterial=new Material(Shader.Find("Unlit/Color")){color=Color.blue};board.GetComponent<Renderer>().sharedMaterial=boardMaterial;
        var portraitHost=Canvas(root,"portrait original canvas");portraitHost.transform.localScale=Vector3.one*.001f;
        portraitHost.transform.localPosition=new Vector3(-.25f,.35f,-.07f);
        var portrait=Rect("displayed portrait",portraitHost.transform,new Vector2(80,80));var face=portrait.gameObject.AddComponent<RawImage>();face.color=Color.yellow;
        var popupHost=Canvas(root,"popup original canvas");popupHost.transform.localScale=Vector3.one*.001f;
        var popup=Rect("native enemy popup",popupHost.transform,new Vector2(180,220));popup.pivot=new Vector2(.23f,.81f);popup.localPosition=new Vector3(47,-19,0);
        var background=popup.gameObject.AddComponent<Image>();background.color=Color.red;var originalMaterial=background.material;
        var textRect=Rect("native text",popup,new Vector2(170,70));textRect.anchoredPosition=new Vector2(20,8);
        var font=Font(inputs);var text=textRect.gameObject.AddComponent<TextMeshProUGUI>();text.font=font;text.fontSize=28;text.text="Enemy card";text.color=Color.white;
        var originalFontMaterial=text.fontSharedMaterial;
        Vector3 nativePose=popup.localPosition;
        var seat=new InitiativePopupPlacement();var treatment=new OnTopUiGraphics(CompareFunction.LessEqual);
        var legacy=new OnTopUiGraphics();legacy.Apply(popup,"historical cache seed");legacy.RestoreAll("seed complete");
        var foreground=GameObject.CreatePrimitive(PrimitiveType.Cube);foreground.name="nearer other player's board";foreground.transform.SetParent(root,false);
        foreground.transform.localScale=new Vector3(.08f,.08f,.02f);
        var frontMaterial=new Material(Shader.Find("Unlit/Color")){color=Color.green};
        var transparentFrontMaterial=new Material(Resources.Load<Shader>("Overlay756")){color=Color.green};transparentFrontMaterial.SetFloat("_ZWrite",0);foreground.GetComponent<Renderer>().sharedMaterial=frontMaterial;foreground.SetActive(false);
        try
        {
            foreach(int scenario in new[]{0,1,2})
            {
                root.position=new Vector3(.1f,-.08f,2.5f);root.rotation=Quaternion.Euler(18+scenario*11,11+scenario*17,scenario*7);
                root.localScale=Vector3.one*(.7f+scenario*.6f);
                portraitHost.transform.localPosition=new Vector3(-.25f+scenario*.22f,.35f,-.07f);
                eye.transform.position=root.TransformPoint(new Vector3(0,.06f,-2));eye.transform.rotation=root.rotation;
                eye.orthographicSize=.65f*root.localScale.x;
                popupHost.overrideSorting=true;popupHost.sortingOrder=scenario==0?100:scenario==1?84:50;
                InitiativePopupPlacement.Order(popup.gameObject,popupHost,0,3);
                seat.Place(popup,portrait,root);treatment.Apply(popup,"world-depth popup");
                var pCorners=new Vector3[4];portrait.GetWorldCorners(pCorners);float low=float.PositiveInfinity,left=float.PositiveInfinity,right=float.NegativeInfinity;
                foreach(Vector3 corner in pCorners){Vector3 local=root.InverseTransformPoint(corner);low=Mathf.Min(low,local.y);left=Mathf.Min(left,local.x);right=Mathf.Max(right,local.x);}
                Vector3 top=root.InverseTransformPoint(popup.TransformPoint(new Vector3(popup.rect.center.x,popup.rect.yMax,0)));
                Check(Mathf.Abs(top.x-(left+right)*.5f)<.00001f,"popup sits directly below its displayed portrait after reorder and board motion");
                Check(Mathf.Abs(top.y-(low-.006f))<.00001f,"popup top remains six board-local millimetres below the portrait");
                Check(top.z<=-.0619f,"popup clears its own opaque rim");
                Vector3 positioned=popup.position;seat.Place(popup,portrait,root);
                Check((popup.position-positioned).sqrMagnitude<1e-12f,"portrait-relative placement is idempotent");
                foreground.SetActive(false);
                var baseline=Capture(eye,Path.Combine(output,label+"-"+scenario+"-visible.png"));
                Vector3 sample=popup.TransformPoint(new Vector3(popup.rect.center.x,popup.rect.center.y-55,0));
                Color clear=Sample(baseline,eye,sample);Check(clear.r>.8f&&clear.g<.2f&&clear.b<.2f,"own board rim does not obscure the readable popup in final pixels");
                Check(WhitePixels(baseline,eye,textRect)>10,"original TMP glyphs are actually present before their occluder appears");
                Object.DestroyImmediate(baseline);
                foreground.transform.position=sample-root.forward*.12f*root.localScale.x;foreground.SetActive(true);
                var covered=Capture(eye,Path.Combine(output,label+"-"+scenario+"-occluded.png"));
                Color occluded=Sample(covered,eye,sample);Check(occluded.g>.8f&&occluded.r<.2f&&occluded.b<.2f,"nearer other board occludes popup in final pixels");
                Object.DestroyImmediate(covered);
                Vector3 textCenter=textRect.TransformPoint(textRect.rect.center);
                foreground.transform.position=textCenter-root.forward*.12f*root.localScale.x;
                foreground.transform.localScale=new Vector3(.20f,.10f,.02f);
                var coveredText=Capture(eye,Path.Combine(output,label+"-"+scenario+"-text-occluded.png"));
                Color textPixel=Sample(coveredText,eye,textCenter);
                Check(textPixel.g>.8f&&textPixel.r<.2f&&textPixel.b<.2f&&WhitePixels(coveredText,eye,textRect)==0,"nearer board occludes original TMP glyph region as well as the card background");
                Object.DestroyImmediate(coveredText);
                foreground.GetComponent<Renderer>().sharedMaterial=transparentFrontMaterial;
                foreground.GetComponent<Renderer>().sortingOrder=popupHost.sortingOrder+16;
                foreground.transform.position=sample-root.forward*.12f*root.localScale.x;
                foreground.transform.localScale=new Vector3(.08f,.08f,.02f);
                var ordered=Capture(eye,Path.Combine(output,label+"-"+scenario+"-transparent-board.png"));
                Color orderedPixel=Sample(ordered,eye,sample);
                Check(orderedPixel.g>.8f&&orderedPixel.r<.2f&&orderedPixel.b<.2f,"nearer depthless board still occludes popup after its owner's draw band changes");
                Object.DestroyImmediate(ordered);foreground.GetComponent<Renderer>().sharedMaterial=frontMaterial;foreground.SetActive(false);
                Check((int)background.material.GetInt("unity_GUIZTestMode")==4,"world-depth clone is not reused from Always");
            }
            Check(text.fontSharedMaterial!=originalFontMaterial,"actual TMP text joins the same world-depth policy");
            treatment.RestoreAll("closed");seat.Restore(popup);
            Check(background.material==originalMaterial&&text.fontSharedMaterial==originalFontMaterial,"popup closure restores exact original graphic and font materials");
            Check((popup.localPosition-nativePose).sqrMagnitude<1e-12f,"popup closure restores its native source pose");
            seat.Place(popup,portrait,root);seat.RestoreAll();Check((popup.localPosition-nativePose).sqrMagnitude<1e-12f,"reveal takeover and teardown restore all native popup seats");
            return _checks+" production geometry/uGUI/TMP/final-pixel assertions";
        }
        finally
        {
            treatment.RestoreAll("fixture teardown");legacy.RestoreAll("fixture teardown");seat.RestoreAll();
            Object.DestroyImmediate(root.gameObject);Object.DestroyImmediate(eye.gameObject);target.Release();Object.DestroyImmediate(target);
            Object.DestroyImmediate(boardMaterial);Object.DestroyImmediate(frontMaterial);Object.DestroyImmediate(transparentFrontMaterial);Object.DestroyImmediate(font.material);Object.DestroyImmediate(font.atlasTexture);Object.DestroyImmediate(font);
        }
    }
}
