using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using GloomhavenVR.WorldUI;
using Object=UnityEngine.Object;
public sealed class ShadowTestImage:Image { public void Populate(VertexHelper vh){base.OnPopulateMesh(vh);} }
public sealed class ShadowTestRawImage:RawImage { public void Populate(VertexHelper vh){base.OnPopulateMesh(vh);} }
public static class WindowShadowFixture {
 [Serializable]public sealed class Report {public bool passed;public int checks,rows,rasterPixels,oldUnderflowPixels;public string failure,unity,device;}
 static Report report=new Report();static List<Object> owned=new List<Object>();static string output;
 static T Keep<T>(T v)where T:Object{owned.Add(v);return v;}
 static void Check(bool v,string m){report.checks++;if(!v)throw new Exception(m);}
 static Rect MeshBounds(Graphic g) {
  using(var vh=new VertexHelper()) {
   if(g is ShadowTestImage)((ShadowTestImage)g).Populate(vh);else((ShadowTestRawImage)g).Populate(vh);
   foreach(var fx in g.GetComponents<IMeshModifier>())fx.ModifyMesh(vh);
   var v=new List<UIVertex>();vh.GetUIVertexStream(v);bool any=false;float x0=0,y0=0,x1=0,y1=0;
   foreach(var vertex in v){if(vertex.color.a==0)continue;var p=g.transform.TransformPoint(vertex.position);if(!any){x0=x1=p.x;y0=y1=p.y;any=true;}else{x0=Mathf.Min(x0,p.x);x1=Mathf.Max(x1,p.x);y0=Mathf.Min(y0,p.y);y1=Mathf.Max(y1,p.y);}}
   return Rect.MinMaxRect(x0,y0,x1,y1);
  }
 }
 static void Near(float a,float b,string why){Check(Mathf.Abs(a-b)<.001f,why+": "+a+" != "+b);}
 static Rect Measure(RectTransform host,Graphic g,string label) {
  Canvas.ForceUpdateCanvases();Rect actual;Check(MrBackingPaintedBounds.TryMeasure(host,g,out actual),"Visible measured "+label);Rect expected=MeshBounds(g);
  Near(actual.xMin,expected.xMin,label+" xMin");Near(actual.xMax,expected.xMax,label+" xMax");Near(actual.yMin,expected.yMin,label+" yMin");Near(actual.yMax,expected.yMax,label+" yMax");report.rows++;return actual;
 }
 public static void Start() {
  string[] a=Environment.GetCommandLineArgs();output=a[Array.IndexOf(a,"-shadowResult")+1];int code=1;
  try{
   report.unity=Application.unityVersion;report.device=SystemInfo.graphicsDeviceName;Check(report.unity=="2021.3.5f1"&&SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null,"Actual Unity GPU");
   Check(typeof(CanvasRenderer).GetMethod("GetMesh")==null,"2021.3.5 public API has no GetMesh");
   var cameraGo=Keep(new GameObject("Owned raster camera"));var c=cameraGo.AddComponent<Camera>();c.enabled=false;c.orthographic=true;c.orthographicSize=128;c.transform.position=new Vector3(0,0,-100);c.nearClipPlane=.1f;c.farClipPlane=200;c.backgroundColor=Color.clear;c.clearFlags=CameraClearFlags.SolidColor;c.allowHDR=false;c.cullingMask=1<<30;
   var rt=Keep(new RenderTexture(256,256,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear));Check(rt.Create(),"Owned raster target");c.targetTexture=rt;
   var root=Keep(new GameObject("Owned canvas",typeof(RectTransform)));root.layer=30;var canvas=root.AddComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.worldCamera=c;var host=root.GetComponent<RectTransform>();host.sizeDelta=new Vector2(256,256);
   bool aspectOnly=Array.IndexOf(a,"-shadowAspectOnly")>=0;
   foreach(bool raw in new[]{false,true}) {
    if(aspectOnly&&raw)continue;
    var go=Keep(new GameObject(raw?"RawImage":"Image",typeof(RectTransform)));go.layer=30;go.transform.SetParent(root.transform,false);Graphic g=raw?(Graphic)go.AddComponent<ShadowTestRawImage>():go.AddComponent<ShadowTestImage>();g.rectTransform.sizeDelta=new Vector2(80,60);g.color=Color.white;
    var shadow=go.AddComponent<Shadow>();shadow.effectDistance=new Vector2(30,-20);shadow.effectColor=new Color(.1f,.1f,.1f,.7f);
    if(aspectOnly) {
        var image=(Image)g;var tex=Keep(new Texture2D(8,16));var texels=new Color[128];for(int i=0;i<texels.Length;i++)texels[i]=Color.white;tex.SetPixels(texels);tex.Apply();image.sprite=Keep(Sprite.Create(tex,new Rect(0,0,8,16),new Vector2(.5f,.5f)));image.preserveAspect=true;
        var fit=Measure(host,g,"preserved-sprite-aspect");Check(fit.width<g.rectTransform.rect.width+30,"Effect expands actual painted image, not padded host rectangle");
        g.color=new Color(1,1,1,.1f);shadow.effectColor=new Color(0,0,0,1f/255);var byteZero=Measure(host,g,"effect-byte-alpha-zero");Near(byteZero.width,30,"Quantized-zero shadow adds no padding");
        shadow.effectColor=Color.clear;shadow.effectDistance=new Vector2(600,-700);Measure(host,g,"transparent-large-shadow");shadow.enabled=false;Measure(host,g,"no-active-effect-exact-old-path");
        go.SetActive(false);continue;
    }
    Rect measured=Measure(host,g,"active-shadow");Check(measured.xMax>g.rectTransform.rect.xMax+8,"Old rectangle plus8 misses real shadow");
    c.Render();RenderTexture old=RenderTexture.active;RenderTexture.active=rt;var pixels=Keep(new Texture2D(256,256,TextureFormat.RGBA32,false,true));pixels.ReadPixels(new Rect(0,0,256,256),0,0,false);pixels.Apply();RenderTexture.active=old;
    var baseRect=g.rectTransform.rect;int underflow=0;
    for(int y=0;y<256;y++)for(int x=0;x<256;x++)if(pixels.GetPixel(x,y).a>.02f){float px=x+.5f-128,py=y+.5f-128;report.rasterPixels++;Check(px>=measured.xMin-.5f&&px<=measured.xMax+.5f&&py>=measured.yMin-.5f&&py<=measured.yMax+.5f,"Actual shadow pixel is backed");if(px>baseRect.xMax+8||py<baseRect.yMin-8)underflow++;}
    Check(underflow>100,"DrawingRect-only causes concrete raster underflow");report.oldUnderflowPixels+=underflow;File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(output),raw?"raw-shadow.png":"image-shadow.png"),pixels.EncodeToPNG());
    shadow.enabled=false;Measure(host,g,"disabled-effect");shadow.enabled=true;shadow.effectDistance=new Vector2(-45,27);Measure(host,g,"changed-offset");shadow.effectColor=Color.clear;Measure(host,g,"zero-alpha-effect");
    // A later alpha-independent shadow can reveal copies generated by a prior
    // transparent modifier. Preserve actual component order and byte alpha.
    var second=go.AddComponent<Shadow>();second.effectDistance=new Vector2(12,14);second.effectColor=new Color(0,0,0,.6f);second.useGraphicAlpha=false;Measure(host,g,"transparent-then-visible-shadow");second.enabled=false;
    var outline=go.AddComponent<Outline>();outline.effectDistance=new Vector2(26,32);outline.effectColor=new Color(0,0,0,.8f);Measure(host,g,"outline");
    shadow.effectColor=new Color(0,0,0,.5f);shadow.enabled=true;Measure(host,g,"mixed-effects");
    g.rectTransform.localRotation=Quaternion.Euler(0,0,21);g.rectTransform.localScale=new Vector3(1.3f,.7f,1);Measure(host,g,"transformed-effects");g.rectTransform.localRotation=Quaternion.identity;g.rectTransform.localScale=Vector3.one;
    var group=go.AddComponent<CanvasGroup>();group.alpha=0;Canvas.ForceUpdateCanvases();Rect hidden;Check(!MrBackingPaintedBounds.TryMeasure(host,g,out hidden),"Native-hidden group cannot grow backing");group.alpha=1;
    g.color=new Color(1,1,1,0);Check(!MrBackingPaintedBounds.TryMeasure(host,g,out hidden),"Zero-alpha native graphic cannot grow backing");g.color=Color.white;
    go.SetActive(false);Check(!MrBackingPaintedBounds.TryMeasure(host,g,out hidden),"Inactive graphic cannot grow backing");go.SetActive(true);
    g.canvasRenderer.SetAlpha(0);Check(!MrBackingPaintedBounds.TryMeasure(host,g,out hidden),"Native own-alpha hide respected");g.canvasRenderer.SetAlpha(1);
    // Existing outer clipping consumes the expanded bounds, not the other way around.
    Rect clipped;Check(MrBackingCaptureBounds.Intersect(measured,new Rect(-20,-20,40,40),out clipped)&&clipped.width==40&&clipped.height==40,"Expanded artwork remains clipped by outer pass");
    go.SetActive(false);
   }
   report.passed=true;code=0;
  }catch(Exception e){report.failure=e.ToString();Debug.LogException(e);}finally{for(int i=owned.Count-1;i>=0;i--)if(owned[i])Object.DestroyImmediate(owned[i]);File.WriteAllText(output,JsonUtility.ToJson(report,true)+"\n");}EditorApplication.Exit(code);
 }
}
