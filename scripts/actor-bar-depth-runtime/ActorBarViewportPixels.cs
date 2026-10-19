// Actual GPU subset control using the real native glyph mesh. This is an explicit
// provider-boundary analogue, not an XR runtime or headset image.
using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;
public static class ActorBarViewportPixels
{
 private static int _checks;
 private static void Check(bool ok,string why) { _checks++; if(!ok) throw new InvalidOperationException(why); }
 private static int Green(Color32[] a) {int n=0;foreach(var p in a)if(p.g>80&&p.g>p.r+25&&p.g>p.b+25)n++;return n;}
 private static int Diff(Color32[] a,Color32[] b) {int n=0;for(int i=0;i<a.Length;i++)if(!a[i].Equals(b[i]))n++;return n;}
 private static Color32[] Read(RenderTexture rt,string path) {var before=RenderTexture.active;RenderTexture.active=rt;var t=new Texture2D(128,128,TextureFormat.RGBA32,false);t.ReadPixels(new Rect(0,0,128,128),0,0);t.Apply();var a=t.GetPixels32();File.WriteAllBytes(path,t.EncodeToPNG());RenderTexture.active=before;Object.DestroyImmediate(t);return a;}
 public static int Run(string evidence,Camera camera,TextMeshProUGUI text,Material original,Material cardMaterial)
 {
  _checks=0;camera.pixelRect=new Rect(0,0,128,128);text.ForceMeshUpdate();Canvas.ForceUpdateCanvases();
  Mesh glyph=text.mesh;Matrix4x4 glyphMatrix=text.transform.localToWorldMatrix;
  var referenceMaterial=new Material(original);referenceMaterial.SetInt("unity_GUIZTestMode",4);
  var bad=new Material(original);bad.shader=Resources.Load<Shader>("DiagnosticBad/ActorBarMobileSDF");bad.CopyPropertiesFromMaterial(original);bad.SetInt("unity_GUIZTestMode",8);
  var good=new Material(original);good.shader=GloomhavenVR.Core.BundleShaders.Resolve("GloomhavenVR/ActorBarMobileSDF","Fixture","actual native depth","missing");good.CopyPropertiesFromMaterial(original);good.SetInt("unity_GUIZTestMode",8);
  Check(bad.shader!=null&&good.shader!=null&&bad.shader.name!=good.shader.name,"negative viewport program and production native derivative resolve separately");
  var card=GameObject.CreatePrimitive(PrimitiveType.Cube);card.name="Native subset witness";card.transform.position=new Vector3(0,0,-2);card.transform.localScale=new Vector3(.85f,.9f,.02f);var renderer=card.GetComponent<Renderer>();renderer.sharedMaterial=cardMaterial;
  var native=new RenderTexture(128,128,24);var incorrect=new RenderTexture(128,128,24);var corrected=new RenderTexture(128,128,24);native.Create();incorrect.Create();corrected.Create();
  var depth=new RenderTexture(new RenderTextureDescriptor(128,128,RenderTextureFormat.Depth,24){msaaSamples=1,shadowSamplingMode=ShadowSamplingMode.None});depth.filterMode=FilterMode.Point;depth.Create();
  var readback=new Texture2D(128,128,TextureFormat.RGBA32,false);var command=new CommandBuffer {name="ActualSubsetViewport"};
  int depthId=Shader.PropertyToID("_GHVRActorForegroundDepth");
  using(var report=new StreamWriter(Path.Combine(evidence,"actual-subset-results.txt")))
  {
   foreach(Rect viewport in new[]{new Rect(0,0,91,83),new Rect(11,7,91,83)})
   {
    foreach(string geometry in new[]{"absent","near","partial","far"})
    {
     bool hasCard=geometry!="absent";card.transform.position=new Vector3(geometry=="partial"?-.25f:0,0,geometry=="far"?.8f:-2);card.transform.localScale=new Vector3(geometry=="partial"?.24f:.85f,.9f,.02f);
     command.Clear();command.SetGlobalVector("_ScreenParams",new Vector4(128,128,1f+1f/128,1f+1f/128));
     command.SetGlobalVector("_DiagnosticViewport",new Vector4(viewport.x,viewport.y,viewport.width,viewport.height));
     command.SetGlobalVector("_GHVRActorForegroundViewport",new Vector4(viewport.x,viewport.y,viewport.width,viewport.height));
     command.SetGlobalVector("_GHVRActorForegroundSize",new Vector4(1f/128,1f/128,128,128));
     command.SetRenderTarget(depth);command.ClearRenderTarget(true,false,Color.clear);command.SetViewport(viewport);if(hasCard)command.DrawRenderer(renderer,cardMaterial,0,0);
     command.SetGlobalTexture(depthId,depth);command.SetGlobalFloat("_GHVRActorForegroundReady",1);
     void Draw(RenderTexture target,Material material) {command.SetRenderTarget(target);command.ClearRenderTarget(true,true,Color.black);command.SetViewport(viewport);if(hasCard)command.DrawRenderer(renderer,cardMaterial,0,0);command.DrawMesh(glyph,glyphMatrix,material,0,0);}
     Draw(native,referenceMaterial);Draw(incorrect,bad);Draw(corrected,good);
     command.SetGlobalFloat("_GHVRActorForegroundReady",0);command.SetGlobalVector("_ScreenParams",new Vector4(128,128,1f+1f/128,1f+1f/128));command.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);command.SetViewport(new Rect(0,0,128,128));
     camera.AddCommandBuffer(CameraEvent.BeforeForwardAlpha,command);try{camera.Render();}finally{camera.RemoveCommandBuffer(CameraEvent.BeforeForwardAlpha,command);}
     string label="subset-"+viewport.x+"-"+viewport.y+"-"+geometry;
     var a=Read(native,Path.Combine(evidence,label+"-native.png"));var b=Read(incorrect,Path.Combine(evidence,label+"-screenparams.png"));var c=Read(corrected,Path.Combine(evidence,label+"-clip.png"));
     int na=Green(a),nb=Green(b),nc=Green(c),badDiff=Diff(a,b),goodDiff=Diff(a,c);report.WriteLine(label+"; allocation=128x128; explicitScreenParams=128x128; actualCommandViewport="+viewport+"; nativeGreen="+na+"; screenParamsGreen="+nb+"; clipGreen="+nc+"; screenParamsDiff="+badDiff+"; clipDiff="+goodDiff);report.Flush();
     if(geometry=="absent"||geometry=="far")Check(na>20,"actual original glyphs render in GPU subset without a nearer card");
     if(geometry=="near")Check(na==0&&nb>0,"allocation-ScreenParams scaling negative control leaves native covered glyphs visible in actual subset");
     if(geometry=="partial")Check(na>0&&badDiff>0,"partial overlap discriminates incorrect allocation-ScreenParams assumption");
     Check(goodDiff==0,"native clip varying matches every original pixel in actual subset "+label);
    }
   }
  }
  command.Release();foreach(var obj in new Object[]{referenceMaterial,bad,good,native,incorrect,corrected,depth,readback,card})Object.DestroyImmediate(obj);return _checks;
 }
}
