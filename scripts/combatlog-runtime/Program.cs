#pragma warning disable CS0649
using System;using System.Collections.Generic;using System.Globalization;using System.IO;using System.Linq;using System.Reflection;using System.Xml;
using GloomhavenVR.WorldUI;using TMPro;using UnityEngine;using UnityEngine.UI;using Object=UnityEngine.Object;
public static class ProgressionTests
{
 static int count;static string inputs="",evidence="";static readonly Dictionary<string,Texture2D> textures=new();static readonly Dictionary<string,TMP_FontAsset> fonts=new();static readonly List<Material> materials=new();
 static void Check(bool value,string message){count++;if(!value)throw new Exception(message);}
 static float[] V(XmlElement e,string name)=>e.GetAttribute(name).Split(',').Select(x=>float.Parse(x,CultureInfo.InvariantCulture)).ToArray();
 static RectTransform Rect(string name,Transform? parent=null){var r=(RectTransform)new GameObject(name,typeof(RectTransform)).transform;r.SetParent(parent,false);r.anchorMin=r.anchorMax=r.pivot=Vector2.one*.5f;return r;}
 static Texture2D Texture(string key){if(textures.TryGetValue(key,out var value))return value;value=new Texture2D(2,2);value.LoadImage(File.ReadAllBytes(Path.Combine(inputs,key+".png")));textures.Add(key,value);return value;}
 [Serializable]sealed class Scalar{public string name="";public float value;}
 [Serializable]sealed class Tint{public string name="";public Color value;}
 [Serializable]sealed class SavedMaterial{public string[] keywords=Array.Empty<string>();public int renderQueue;public Scalar[] floats=Array.Empty<Scalar>();public Tint[] colors=Array.Empty<Tint>();}
 static TMP_FontAsset Font(string key)
 {
  if(fonts.TryGetValue(key,out var font))return font;font=ScriptableObject.CreateInstance<TMP_FontAsset>();JsonUtility.FromJsonOverwrite(File.ReadAllText(Path.Combine(inputs,key+".json")),font);
  font.atlasTextures=new[]{Texture(key+"-atlas")};font.atlasPopulationMode=AtlasPopulationMode.Static;var material=new Material(Resources.Load<Shader>("OriginalShaders/NativeSDF"));material.mainTexture=font.atlasTextures[0];materials.Add(material);
  var saved=JsonUtility.FromJson<SavedMaterial>(File.ReadAllText(Path.Combine(inputs,key+"-material.json")));material.shaderKeywords=saved.keywords;material.renderQueue=saved.renderQueue;foreach(var x in saved.floats)material.SetFloat(x.name,x.value);foreach(var x in saved.colors)material.SetColor(x.name,x.value);font.material=material;font.ReadFontAssetDefinition();fonts.Add(key,font);return font;
 }
 static RectTransform Import(XmlElement node,Transform parent)
 {
  var r=Rect(node.GetAttribute("name"),parent);r.gameObject.SetActive(false);
  foreach(XmlElement c in node.SelectNodes("component"))
  {
   string kind=c.GetAttribute("kind"),json=c.GetAttribute("json");
   if(kind=="RectTransform")
   {
    var a=V(c,"m_AnchorMin");r.anchorMin=new Vector2(a[0],a[1]);a=V(c,"m_AnchorMax");r.anchorMax=new Vector2(a[0],a[1]);a=V(c,"m_Pivot");r.pivot=new Vector2(a[0],a[1]);a=V(c,"m_SizeDelta");r.sizeDelta=new Vector2(a[0],a[1]);a=V(c,"m_AnchoredPosition");r.anchoredPosition=new Vector2(a[0],a[1]);a=V(c,"m_LocalPosition");r.localPosition=new Vector3(r.localPosition.x,r.localPosition.y,a[2]);a=V(c,"m_LocalRotation");r.localRotation=new Quaternion(a[0],a[1],a[2],a[3]);a=V(c,"m_LocalScale");r.localScale=new Vector3(a[0],a[1],a[2]);
   }
   else if(kind=="Image")
   {
    var image=r.gameObject.AddComponent<Image>();JsonUtility.FromJsonOverwrite(json,image);
    // Non-font Windows image fragment programs are a declared GL presentation port.
    // Every original sprite/rect/color/type stays authored; no native controller runs.
    if(c.HasAttribute("sprite")){var texture=Texture(c.GetAttribute("sprite"));var pivot=V(c,"pivot");var border=V(c,"border");image.sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(pivot[0],pivot[1]),float.Parse(c.GetAttribute("ppu"),CultureInfo.InvariantCulture),0,SpriteMeshType.FullRect,new Vector4(border[0],border[1],border[2],border[3]));}
   }
   else if(kind=="TextMeshProUGUI"){var text=r.gameObject.AddComponent<TextMeshProUGUI>();JsonUtility.FromJsonOverwrite(json,text);text.font=Font(c.GetAttribute("font"));text.fontSharedMaterial=text.font.material;}
   else if(kind=="RectMask2D")JsonUtility.FromJsonOverwrite(json,r.gameObject.AddComponent<RectMask2D>());
   else if(kind=="Mask")JsonUtility.FromJsonOverwrite(json,r.gameObject.AddComponent<Mask>());
   else if(kind=="VerticalLayoutGroup")JsonUtility.FromJsonOverwrite(json,r.gameObject.AddComponent<VerticalLayoutGroup>());
   else if(kind=="HorizontalLayoutGroup")JsonUtility.FromJsonOverwrite(json,r.gameObject.AddComponent<HorizontalLayoutGroup>());
   else if(kind=="ContentSizeFitter")JsonUtility.FromJsonOverwrite(json,r.gameObject.AddComponent<ContentSizeFitter>());
   else if(kind=="LayoutElement")JsonUtility.FromJsonOverwrite(json,r.gameObject.AddComponent<LayoutElement>());
  }
  foreach(XmlElement child in node.SelectNodes("node"))Import(child,r);
  r.gameObject.SetActive(node.GetAttribute("active")=="1");return r;
 }
 static RectTransform Load(string file,Transform parent){var doc=new XmlDocument();doc.Load(Path.Combine(inputs,file+".xml"));return Import(doc.DocumentElement!,parent);}
 static RectTransform Find(RectTransform root,string name)=>root.GetComponentsInChildren<RectTransform>(true).First(x=>x.name==name);
 sealed class PixelFrame{internal byte[] Bytes=Array.Empty<byte>();internal int Glyphs;}
 static PixelFrame Render(Camera camera,string name)
 {
  Canvas.ForceUpdateCanvases();foreach(var text in Object.FindObjectsOfType<TextMeshProUGUI>())text.ForceMeshUpdate(true,true);camera.Render();RenderTexture.active=camera.targetTexture;
  var texture=new Texture2D(camera.targetTexture.width,camera.targetTexture.height,TextureFormat.RGBA32,false);texture.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);texture.Apply();
  File.WriteAllBytes(Path.Combine(evidence,name+".png"),texture.EncodeToPNG());var result=new PixelFrame{Bytes=texture.GetRawTextureData().ToArray(),Glyphs=texture.GetPixels32().Count(x=>x.r>160&&x.g>160&&x.b>160)};Object.DestroyImmediate(texture);RenderTexture.active=null;return result;
 }
 public static int Run(string output)
 {
  count=0;evidence=output;inputs=Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(output))!,"inputs");
  typeof(TMP_Settings).GetField("s_Instance",BindingFlags.NonPublic|BindingFlags.Static)!.SetValue(null,ScriptableObject.CreateInstance<TMP_Settings>());
  // The real preloader normally supplies this before ConfigFile's core-file
  // initializer. This fixture owns only a temporary path, not plugin startup.
  typeof(BepInEx.Paths).GetProperty("BepInExConfigPath")!.GetSetMethod(true)!.Invoke(null,new object[]{Path.Combine(output,"BepInEx.cfg")});
  WorldUIConfig.Bind(Path.Combine(output,"actual-bepinex.cfg"));WorldUIConfig.CombatLogScale.Value=.2f;Check(Mathf.Approximately(WorldUIConfig.CombatLogScale.Value,.2f),"actual config range admits the same small two-hand gesture as the shared handle");
  WorldUIConfig.CombatLogScale.Value=.01f;Check(WorldUIConfig.CombatLogScale.Value==PanelGrabHandle.MinScale,"actual config rejects sizes below the shared handle minimum");WorldUIConfig.CombatLogScale.Value=4;Check(WorldUIConfig.CombatLogScale.Value==PanelGrabHandle.MaxScale,"actual config rejects sizes above the shared handle maximum");
  var head=new GameObject("actual final head camera").AddComponent<Camera>();head.orthographic=true;head.orthographicSize=.36f;head.aspect=16f/9;head.nearClipPlane=.001f;head.farClipPlane=10000;head.clearFlags=CameraClearFlags.SolidColor;head.backgroundColor=Color.black;head.cullingMask=1<<24;head.targetTexture=new RenderTexture(1280,720,24);CanvasConversion.WorldCamera=head;
  int schedule=0;
  foreach(float worldScale in new[]{.2f,1f,21.9655f})
  foreach(Vector2 desktop in new[]{new Vector2(1920,1080),new Vector2(3440,1440)})
  {
   var flat=new GameObject("unrendered native desktop camera").AddComponent<Camera>();flat.enabled=false;flat.aspect=desktop.x/desktop.y;
   var nativeRoot=Rect("original native desktop root");nativeRoot.sizeDelta=new Vector2(desktop.x/desktop.y*1080,1080);var nativeCanvas=nativeRoot.gameObject.AddComponent<Canvas>();nativeCanvas.renderMode=RenderMode.ScreenSpaceCamera;nativeCanvas.worldCamera=flat;nativeCanvas.planeDistance=1;
   var scaler=nativeRoot.gameObject.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.enabled=false;
   // These independent literal dimensions are asserted by the read-only original
   // level6/3193 exporter; the real child has full local stretch and zero insets.
   var fixedParent=Rect("original CombatLogPanel level6/3193",nativeRoot);fixedParent.sizeDelta=new Vector2(569.400024f,291.100006f);
   var target=Load("window",fixedParent);target.gameObject.SetActive(true);target.gameObject.AddComponent<UIWindow>();target.gameObject.AddComponent<CombatLogHandler>();Canvas.ForceUpdateCanvases();
   Check((target.rect.size-fixedParent.rect.size).sqrMagnitude<.001f,"native log local stretch fills only its compact fixed parent");
   var host=Rect("physical combat-log host");host.gameObject.layer=24;var canvas=host.gameObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.worldCamera=head;
   foreach(var r in target.GetComponentsInChildren<RectTransform>(true))r.gameObject.layer=24;
   var panel=new ConvertedPanel{HostGo=host.gameObject,HostRect=host};Vector2 captured=NativeFrameConversion764.Convert(target,host,panel);
   Check((captured-fixedParent.rect.size).sqrMagnitude<.001f,"conversion retains original compact combat-log frame instead of full desktop reference");
   Check(panel.NativeDesignFrame==Vector2.zero,"native compact combat log never acquires full-screen normalization");
   var content=Find(target,"Content");var row=Load("combatLogText",content);row.gameObject.SetActive(true);var text=row.GetComponentInChildren<TextMeshProUGUI>(true);text.gameObject.SetActive(true);text.text="Brute attacks Bandit Guard";text.ForceMeshUpdate(true,true);
   // Native log population is a scheduling port, not gameplay. Original standard
   // layout groups/fitters place the row and size its originally zero-width label.
   // A raw template without those components produces vertical wrapping; assert
   // actual final glyph geometry below instead of accepting any white pixels.
   foreach(var r in row.GetComponentsInChildren<RectTransform>(true))r.gameObject.layer=24;
   foreach(var r in target.GetComponentsInChildren<RectTransform>(true)){r.localRotation=Quaternion.identity;var p=r.localPosition;r.localPosition=new Vector3(p.x,p.y,0);}
   var frame=new GameObject("real grab root").transform;var anchor=new GameObject("rig-or-frozen-pin anchor").transform;anchor.localScale=Vector3.one*worldScale;frame.SetParent(anchor,false);frame.localPosition=new Vector3(0,-.16f,2);frame.localScale=Vector3.one;
   PanelLayout.WorldScale=worldScale;PanelLayout.Anchor=Vector3.zero;head.orthographicSize=.36f*worldScale;head.transform.position=Vector3.zero;
   CombatLogProjection.Configure(panel,frame);WorldUIConfig.CombatLogScale.Value=1;CombatLogProjection.Tick();
   LayoutRebuilder.ForceRebuildLayoutImmediate(content);Canvas.ForceUpdateCanvases();text.ForceMeshUpdate(true,true);
   Check(text.textInfo.lineCount==1&&text.rectTransform.rect.width>300f,"original native layout produces a readable single-line log entry");
   var corners=new Vector3[4];text.rectTransform.GetWorldCorners(corners);foreach(var corner in corners){Vector3 local=host.InverseTransformPoint(corner);Check(host.rect.Contains(new Vector2(local.x,local.y)),"native entry glyph rectangle stays within the compact physical log");}
   Vector3 savedPosition=frame.position;Vector3 acceptedScale=host.lossyScale;
   var before=Render(head,"schedule-"+schedule+"-native-size");Check(before.Glyphs>100,"original compact log produces readable native glyph pixels in final head image");
   foreach(float factor in new[]{.2f,.37f,.8f,1.51f})
   {
    // This is the output of the real handle's already-qualified two-hand gesture;
    // the changed surface's post-release consumer and real config run here.
    CombatLogProjection.Grip(true);frame.localScale=Vector3.one*factor;CombatLogProjection.Tick();CombatLogProjection.Grip(false);CombatLogProjection.Release();
    Check(Mathf.Abs(WorldUIConfig.CombatLogScale.Value-factor)<1e-5f,"release retains the complete small or large manual resize in actual config");
    CombatLogProjection.Tick();Check(Mathf.Abs(frame.localScale.x-factor)<1e-5f,"free placement tick preserves the manual resize without snapping");
    Check(frame.position==savedPosition,"manual resize does not invoke a new window spawn");
    var released=Render(head,"schedule-"+schedule+"-factor-"+factor.ToString("0.00",CultureInfo.InvariantCulture)+"-release");
    for(int i=0;i<5;i++){nativeRoot.sizeDelta=i%2==0?new Vector2(2580,1080):new Vector2(1920,1080);NativeFrameConversion764.Maintain(target,panel.NativeDesignFrame);CombatLogProjection.Tick();}
    var settled=Render(head,"schedule-"+schedule+"-factor-"+factor.ToString("0.00",CultureInfo.InvariantCulture)+"-settled");
    Check(released.Bytes.SequenceEqual(settled.Bytes),"manual resize and text remain pixel-stable through desktop/window-mode transitions");
    Check((host.rect.size-captured).sqrMagnitude<.001f,"desktop changes never enlarge the original compact log frame");
   }
   WorldUIConfig.CombatLogScale.Value=.2f;CombatLogProjection.Respawn();Check(Mathf.Abs(frame.localScale.x-.2f)<1e-5f,"explicit log respawn preserves the full supported small scale");
   Object.DestroyImmediate(anchor.gameObject);Object.DestroyImmediate(host.gameObject);Object.DestroyImmediate(nativeRoot.gameObject);Object.DestroyImmediate(flat.gameObject);schedule++;
  }
  head.targetTexture.Release();Object.DestroyImmediate(head.targetTexture);Object.DestroyImmediate(head.gameObject);return count;
 }
 public static void Cleanup(){foreach(var f in fonts.Values)Object.DestroyImmediate(f);foreach(var m in materials)Object.DestroyImmediate(m);foreach(var t in textures.Values)Object.DestroyImmediate(t);fonts.Clear();materials.Clear();textures.Clear();}
}
