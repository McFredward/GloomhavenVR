using System;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Board.FigureGrab;
using GloomhavenVR.Core;
using GloomhavenVR.Rig;
using GloomhavenVR.WorldUI;
using TMPro;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static class NativeFontProgram
{
    private static int _checks;
    private static void Check(bool value,string message) {_checks++;if(!value)throw new InvalidOperationException(message);}
    private static int Green(Color32[] pixels)
    {int n=0;foreach(var p in pixels)if(p.g>80&&p.g>p.r+25&&p.g>p.b+25)n++;return n;}
    private static int Ink(Color32[] pixels)
    {int n=0;foreach(var p in pixels)if(p.r>80||p.g>80||p.b>80)n++;return n;}
    [Serializable] public sealed class SavedFloat {public string name="";public float value=0;}
    [Serializable] public sealed class SavedColor {public string name="";public Color value=Color.clear;}
    [Serializable] public sealed class SavedTexture {public string name="";public int path_id=0;public Vector2 scale=Vector2.one,offset=Vector2.zero;}
    [Serializable] public sealed class SavedMaterial {public string name="";public int shader_path_id=0,renderQueue=0;public string[] keywords=Array.Empty<string>();public SavedFloat[] floats=Array.Empty<SavedFloat>();public SavedColor[] colors=Array.Empty<SavedColor>();public SavedTexture[] textures=Array.Empty<SavedTexture>();}
    private static bool Same(Color32[] a,Color32[] b)
    {if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(!a[i].Equals(b[i]))return false;return true;}
    public static int Run(string evidence)
    {
        _checks=0;
        FixtureNativeSettings.Ensure();
        string inputs=Path.GetDirectoryName(evidence)!;
        var atlas=new Texture2D(2,2,TextureFormat.RGBA32,false);atlas.LoadImage(File.ReadAllBytes(Path.Combine(inputs,"native-font-atlas.png")));
        var font=ScriptableObject.CreateInstance<TMP_FontAsset>();JsonUtility.FromJsonOverwrite(File.ReadAllText(Path.Combine(inputs,"native-font.json")),font);
        font.atlasTextures=new[]{atlas};font.atlasPopulationMode=AtlasPopulationMode.Static;
        var original=new Material(Resources.Load<Shader>("OriginalShaders/ActorBarMobileSDF"));
        original.SetTexture("_MainTex",atlas);original.SetColor("_FaceColor",Color.green);
        original.SetFloat("_TextureWidth",atlas.width);original.SetFloat("_TextureHeight",atlas.height);
        original.SetFloat("_GradientScale",10);original.SetFloat("_WeightNormal",0);original.SetFloat("_WeightBold",.5f);
        original.SetFloat("_ScaleX",1);original.SetFloat("_ScaleY",1);original.SetFloat("_ScaleRatioA",1);
        original.SetFloat("_ScaleRatioB",1);original.SetFloat("_ScaleRatioC",1);font.material=original;
        font.ReadFontAssetDefinition();
        Check(font.characterLookupTable.ContainsKey('1')&&font.characterLookupTable.ContainsKey('2'),"actual game font contains the original health-number glyphs");
        var host=new GameObject("Native original full-font bar",typeof(RectTransform),typeof(Canvas));var canvas=host.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.sortingOrder=90;
        var numberGo=new GameObject("Actual native TMP health number",typeof(RectTransform));numberGo.transform.SetParent(host.transform,false);
        var number=numberGo.AddComponent<TextMeshProUGUI>();number.font=font;number.fontSharedMaterial=original;
        number.fontSize=66;number.alignment=TextAlignmentOptions.Center;number.text="12";number.rectTransform.sizeDelta=new Vector2(200,100);number.transform.localScale=Vector3.one*.01f;
        var cameraGo=new GameObject("Native font camera");var camera=cameraGo.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=1.5f;
        camera.transform.position=new Vector3(0,0,-5);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
        var target=new RenderTexture(128,128,24);target.Create();camera.targetTexture=target;VRRigDriver.HeadCamera=camera;canvas.worldCamera=camera;
        var read=new Texture2D(128,128,TextureFormat.RGBA32,false);
        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="GloomhavenVR.Room";wall.transform.position=new Vector3(0,0,-1);wall.transform.localScale=new Vector3(4,3,.1f);
        wall.layer=VRLayers.ModLayer;
        var wallMat=new Material(Shader.Find("Unlit/Color"));wallMat.color=Color.black;wall.GetComponent<Renderer>().sharedMaterial=wallMat;
        HealthbarForegroundDepth.Register(wall.transform);
        Color32[] Render(string label,bool capture=true)
        {
            number.ForceMeshUpdate();Canvas.ForceUpdateCanvases();
            if(capture)HealthbarForegroundDepth.Prepare(camera);
            // The camera clears only its viewport. The test reads the complete
            // allocation, so remove old pixels outside a reduced viewport first.
            var before=RenderTexture.active;RenderTexture.active=target;GL.Clear(true,true,Color.black);RenderTexture.active=before;
            camera.Render();var previous=RenderTexture.active;RenderTexture.active=target;read.ReadPixels(new Rect(0,0,128,128),0,0);read.Apply();RenderTexture.active=previous;
            File.WriteAllBytes(Path.Combine(evidence,label+".png"),read.EncodeToPNG());return read.GetPixels32();
        }
        var owner=ActorBars.Create(host);
        wall.SetActive(false);var firstOriginal=Render("native-font-first-original",false);
        File.WriteAllText(Path.Combine(evidence,"native-font-binding.txt"),"initialOriginalPixels="+Green(firstOriginal)+"\nshader="+number.fontSharedMaterial.shader.name+"\ncharCount="+number.textInfo.characterCount+"\nvertexCount="+number.textInfo.meshInfo[0].vertexCount+"\natlas="+atlas.width+"x"+atlas.height+"\nfont="+font.faceInfo.familyName+"\n");
        Check(Green(firstOriginal)>20&&number.textInfo.characterCount==2,"canonical original shader and actual native game glyphs render before capture instrumentation");
        _checks+=ActorBarViewportPixels.Run(evidence,camera,number,original,wallMat);
        _checks+=ActorBarXrViewportPixels.Run(evidence,camera,number,original,wallMat);wall.SetActive(true);
        ActorBars.Scan(owner,true);Check(Green(Render("native-font-wall-hidden"))==0,"actual native font health numbers are hidden by walls only when requested");
        ActorBars.Restore(owner);ActorBars.Scan(owner,false);HealthbarForegroundDepth.SetActive(true);
        var baseline=Render("native-font-through-wall");int visible=Green(baseline);Check(visible>20,"original game atlas and real TMP glyph tessellation render through a wall");
        Check(number.textInfo.characterCount==2&&number.textInfo.meshInfo[0].vertexCount>=8,"native glyph geometry, not replacement test rectangles, executes");
        wall.SetActive(false);
        ActorBars.Restore(owner);number.fontSharedMaterial=original;original.SetInt("unity_GUIZTestMode",8);
        HealthbarForegroundDepth.SetActive(false);var nativePixels=Render("native-original-font-pixels",false);
        ActorBars.Scan(owner,false);HealthbarForegroundDepth.SetActive(true);var derivedPixels=Render("native-derived-font-pixels");
        Check(Same(nativePixels,derivedPixels),"derivative preserves original native glyph pixel colour/shape without foreground");
        wall.SetActive(true);
        HealthbarForegroundDepth.SetActive(false);
        Check(Green(Render("capture-unavailable-native-depth",false))==0,"capture unavailable keeps own derivative on safe ordinary depth rather than piercing foreground");
        Check(number.fontSharedMaterial.GetInt("unity_GUIZTestMode")==4,"capture unavailable selects ordinary native depth on each owned material");
        HealthbarForegroundDepth.SetActive(true);
        Check(Green(Render("capture-recovered-native-depth"))==visible&&number.fontSharedMaterial.GetInt("unity_GUIZTestMode")==8,"successful capture restores complete through-wall mode on the same native clone");
        GameObject Solid(string name,float z,float x=0,float width=.85f)
        {
            var root=new GameObject(name);root.layer=VRLayers.ModLayer;var mesh=GameObject.CreatePrimitive(PrimitiveType.Cube);mesh.transform.SetParent(root.transform,false);mesh.layer=VRLayers.ModLayer;
            mesh.transform.position=new Vector3(x,0,z);mesh.transform.localScale=new Vector3(width,.9f,.02f);mesh.GetComponent<Renderer>().sharedMaterial=wallMat;
            HealthbarForegroundDepth.Register(root.transform);return root;
        }
        var card=Solid("VRCard",-2);
        Check(Green(Render("solid-nearer-card"))==0,"actual nearer solid card geometry hides every covered native glyph pixel");
        var duplicate=new Material(Shader.Find("GloomhavenVR/HealthbarDuplicateForward"));var cardRenderer=card.GetComponentInChildren<Renderer>();cardRenderer.sharedMaterial=duplicate;
        File.WriteAllText(Path.Combine(evidence,"duplicate-forward-pass.txt"),"passCount="+duplicate.passCount+"\nFindPassFORWARD="+duplicate.FindPass("FORWARD")+"\n");
        Check(duplicate.passCount==2&&Green(Render("duplicate-forward-base-depth"))==0,"duplicate FORWARD names select actual depth-writing ForwardBase by LightMode");cardRenderer.sharedMaterial=wallMat;
        card.transform.GetChild(0).position=new Vector3(0,0,.8f);
        Check(Green(Render("solid-farther-card"))==visible,"farther solid foreground does not hide a nearer native healthbar");
        card.transform.GetChild(0).position=new Vector3(-.25f,0,-2);card.transform.GetChild(0).localScale=new Vector3(.24f,.9f,.02f);
        int partial=Green(Render("solid-partial-card"));Check(partial>0&&partial<visible,"partial card overlap clips covered pixels without hiding the whole native widget");
        card.SetActive(false);Check(Green(Render("inactive-card"))==visible,"deactivated physical card immediately stops occluding");
        var remote=Solid("GloomhavenVR.RemoteHandFan[42]",-2);
        Check(Green(Render("remote-nearer-card"))==0,"remote physical cards use the same selective depth rule");remote.SetActive(false);
        var held=Solid("unmodified native held figure",-2);held.transform.GetChild(0).gameObject.layer=0;
        var heldRenderer=held.GetComponentInChildren<Renderer>();
        FigureGrabbable.Left=new FigureGrabbable {Renderers=new[]{heldRenderer}};
        Check(Green(Render("local-native-held-left"))==0,"native figure held in the left hand occludes without changing its game layer");
        FigureGrabbable.Left=null;FigureGrabbable.Right=new FigureGrabbable {Renderers=new[]{heldRenderer}};
        Check(Green(Render("local-native-held-right"))==0,"native figure held in the right hand uses the same depth rule");
        FigureGrabbable.Right=null;Check(Green(Render("local-native-held-release"))==visible,"released native figure immediately leaves foreground and never becomes sticky world geometry");held.SetActive(false);
        var hand=Solid("GloomhavenVR.Hands",-2);
        var ghost=new Material(Shader.Find("GloomhavenVR/BoardLit"));ghost.SetColor("_Color",new Color(.1f,.1f,1));ghost.SetFloat("_FadeAlpha",.35f);
        ghost.SetFloat("_SrcBlend",5);ghost.SetFloat("_DstBlend",10);ghost.SetFloat("_ZWrite",0);ghost.renderQueue=3100;
        var handRenderer=hand.GetComponentInChildren<Renderer>();handRenderer.sharedMaterial=ghost;handRenderer.sortingOrder=100;
        int ghostPixels=Green(Render("native-ghost-hand-blend"));
        Check(ghostPixels>0,"native ghost-hand alpha blend keeps underlying health glyphs visible; it is never a binary depth stamp");
        Check(ghost.GetFloat("_ZWrite")==0&&Math.Abs(ghost.GetFloat("_FadeAlpha")-.35f)<.0001f,"capture preserves original ghost material state");hand.SetActive(false);
        var far=Solid("GloomhavenVR.RemoteControlBoard[42]",.8f);
        foreach(float eye in new[]{-.035f,.035f})
        {
            camera.transform.position=new Vector3(eye,.03f,-5);var eyePixels=Render("eye-"+eye.ToString("F3"));
            Check(Green(eyePixels)>20,"per-eye capture uses the actual off-centre camera and empty far depth");
            far.transform.GetChild(0).position=new Vector3(0,0,-2);
            Check(Green(Render("eye-occluded-"+eye.ToString("F3")))==0,"each distinct eye correctly clips actual nearer foreground");
            far.transform.GetChild(0).position=new Vector3(0,0,.8f);
        }
        camera.transform.position=new Vector3(0,0,-5);far.SetActive(false);
        if(!File.Exists(Path.Combine(inputs,"skip-viewport.marker")))
        {
        camera.pixelRect=new Rect(13,17,91,83);
        wall.SetActive(false);ActorBars.Restore(owner);original.SetInt("unity_GUIZTestMode",4);HealthbarForegroundDepth.SetActive(false);
        var viewportOriginal=Render("viewport-reduced-original",false);
        ActorBars.Scan(owner,false);HealthbarForegroundDepth.SetActive(true);var viewportDerived=Render("viewport-reduced-derived");
        Check(Same(viewportOriginal,viewportDerived),"reduced offset viewport preserves original native pixel domain after both target switches");
        card.SetActive(true);card.transform.GetChild(0).position=new Vector3(0,0,-2);card.transform.GetChild(0).localScale=new Vector3(.85f,.9f,.02f);
        var viewportCardDerived=Render("viewport-reduced-card");
        ActorBars.Restore(owner);original.SetInt("unity_GUIZTestMode",4);HealthbarForegroundDepth.SetActive(false);
        var viewportCardOriginal=Render("viewport-reduced-card-original",false);
        File.WriteAllText(Path.Combine(evidence,"native-viewport-pixels.txt"),"originalNoCard="+Green(viewportOriginal)+"\noriginalCard="+Green(viewportCardOriginal)+"\nderivedCard="+Green(viewportCardDerived)+"\n");
        Check(Same(viewportCardOriginal,viewportCardDerived)&&Green(viewportCardDerived)<Green(viewportOriginal),"target-texture viewport matches native full-target card occlusion despite ignored camera rect");
        ActorBars.Scan(owner,false);HealthbarForegroundDepth.SetActive(true);
        card.SetActive(false);camera.pixelRect=new Rect(0,0,128,128);wall.SetActive(true);
        }
        number.text="21";Check(Green(Render("native-health-update"))>20&&number.textInfo.characterCount==2,"native later health-number updates retain original TMP controller rendering");

        // The original game's Sarala material uses the full Distance Field shader.
        // Exercise its real glyph/outline/underlay path, independently of Mobile.
        ActorBars.Restore(owner);HealthbarForegroundDepth.SetActive(false);
        var full=new Material(Resources.Load<Shader>("OriginalShaders/ActorBarSDF"));
        Check(full.HasProperty("_FaceTex")&&full.HasProperty("_OutlineTex"),"canonical full SDF source resolves its authored face and outline texture properties");
        var saved=JsonConvert.DeserializeObject<SavedMaterial>(File.ReadAllText(Path.Combine(inputs,"native-font-material.json")))!;
        File.WriteAllText(Path.Combine(evidence,"native-material202-binding.txt"),"name="+saved.name+"\nshaderPath="+saved.shader_path_id+"\nfloats="+saved.floats.Length+"\ncolours="+saved.colors.Length+"\ntextures="+saved.textures.Length+"\nqueue="+saved.renderQueue+"\n");
        Check(saved.floats.Length>30&&saved.colors.Length>8&&saved.textures.Length==5,"original font material202 serialized floats colours and textures all deserialize");
        Check(saved.shader_path_id==568,"actual original font material202 references original full SDF shader568");
        full.name=saved.name;full.shaderKeywords=saved.keywords;full.renderQueue=saved.renderQueue;
        foreach(var property in saved.floats)full.SetFloat(property.name,property.value);
        foreach(var property in saved.colors)full.SetColor(property.name,property.value);
        foreach(var property in saved.textures){Check(property.path_id==0||property.path_id==440,"original font material texture binding is the actual game atlas or authored null");full.SetTexture(property.name,property.path_id==440?atlas:null);full.SetTextureScale(property.name,property.scale);full.SetTextureOffset(property.name,property.offset);}
        Check(full.GetTexture("_MainTex")==atlas&&Math.Abs(full.GetFloat("_GradientScale")-6f)<.0001f,"original font material202 retains its actual atlas and gradient scale");
        full.SetInt("unity_GUIZTestMode",8);
        number.fontSharedMaterial=full;wall.SetActive(false);var fullOriginal=Render("full-sdf-original",false);
        File.WriteAllText(Path.Combine(evidence,"full-sdf-binding.txt"),"shader="+full.shader.name+"\nsupported="+full.shader.isSupported+"\nface="+full.GetColor("_FaceColor")+"\nmain="+(full.GetTexture("_MainTex")!=null)+"\nfaceTex="+(full.GetTexture("_FaceTex")!=null)+"\nscale="+full.GetFloat("_GradientScale")+"\nkeywords="+string.Join(",",full.shaderKeywords)+"\nfontVertices="+number.textInfo.meshInfo[0].vertexCount+"\nfontCharacters="+number.textInfo.characterCount+"\nvisible="+Ink(fullOriginal)+"\n");
        Check(Ink(fullOriginal)>20,"full native Distance Field font renders actual original game glyphs");
        ActorBars.Scan(owner,false);HealthbarForegroundDepth.SetActive(true);var fullDerived=Render("full-sdf-derived");
        Check(Same(fullOriginal,fullDerived),"full native SDF outline/underlay/glyph pixels survive exact derivative binding");
        wall.SetActive(true);card.SetActive(true);card.transform.GetChild(0).position=new Vector3(0,0,-2);card.transform.GetChild(0).localScale=new Vector3(.85f,.9f,.02f);
        Check(Ink(Render("full-sdf-nearer-card"))==0,"full native SDF glyphs respect actual nearer solid card depth");
        card.transform.GetChild(0).position=new Vector3(0,0,.8f);
        Check(Same(fullDerived,Render("full-sdf-farther-card")),"full native SDF preserves all original pixels in front of farther foreground");card.SetActive(false);

        ActorBars.Restore(owner);HealthbarForegroundDepth.SetActive(false);wall.SetActive(false);
        camera.orthographic=false;camera.fieldOfView=33f;camera.pixelRect=new Rect(0,0,128,128);full.SetInt("unity_GUIZTestMode",4);
        foreach(string geometry in new[]{"absent","near","partial","far"})
        {
            card.SetActive(geometry!="absent");card.transform.GetChild(0).position=new Vector3(geometry=="partial"?-.15f:0,0,geometry=="far"?.8f:-2);
            card.transform.GetChild(0).localScale=new Vector3(geometry=="partial"?.14f:.85f,.9f,.02f);
            var nativePerspective=Render("perspective-"+geometry+"-original",false);
            ActorBars.Scan(owner,false);HealthbarForegroundDepth.SetActive(true);var derivedPerspective=Render("perspective-"+geometry+"-derived");
            Check(Same(nativePerspective,derivedPerspective),"perspective native clip varying preserves original per-pixel "+geometry+" depth");
            if(geometry=="near")Check(Ink(derivedPerspective)==0,"perspective nearer foreground clips all original native glyphs");
            else Check(Ink(derivedPerspective)>20,"perspective original glyphs remain visible without full near overlap");
            ActorBars.Restore(owner);HealthbarForegroundDepth.SetActive(false);
        }
        camera.orthographic=true;card.SetActive(false);
        full.SetInt("unity_GUIZTestMode",8);
        full.SetFloat("_OutlineWidth",.08f);full.SetFloat("_UnderlayOffsetX",.6f);full.SetFloat("_UnderlayOffsetY",.6f);full.EnableKeyword("UNDERLAY_ON");number.UpdateMeshPadding();
        var styledOriginal=Render("full-sdf-original-outline-underlay",false);
        ActorBars.Scan(owner,false);HealthbarForegroundDepth.SetActive(true);var styledDerived=Render("full-sdf-derived-outline-underlay");
        Check(Same(styledOriginal,styledDerived)&&Ink(styledOriginal)>20,"additional native full-SDF outline and underlay branches remain pixel-identical");
        wall.SetActive(true);card.SetActive(true);card.transform.GetChild(0).position=new Vector3(0,0,-2);
        Check(Ink(Render("full-sdf-outline-underlay-nearer-card"))==0,"nearer card clips native full SDF outlines and underlay together");card.SetActive(false);

        var spriteAtlas=new Texture2D(2,2,TextureFormat.RGBA32,false);spriteAtlas.LoadImage(File.ReadAllBytes(Path.Combine(inputs,"native-sprite-atlas.png")));
        var sprite=ScriptableObject.CreateInstance<TMP_SpriteAsset>();JsonUtility.FromJsonOverwrite(File.ReadAllText(Path.Combine(inputs,"native-sprite.json")),sprite);
        var spriteMaterial=new Material(Resources.Load<Shader>("OriginalShaders/ActorBarSprite"));spriteMaterial.SetTexture("_MainTex",spriteAtlas);spriteMaterial.SetInt("unity_GUIZTestMode",8);
        sprite.spriteSheet=spriteAtlas;sprite.material=spriteMaterial;sprite.UpdateLookupTables();
        ActorBars.Restore(owner);HealthbarForegroundDepth.SetActive(false);number.spriteAsset=sprite;number.text="12<sprite=0>";wall.SetActive(false);
        var spriteOriginal=Render("native-inline-sprite-original",false);
        Check(number.textInfo.materialCount==2&&number.textInfo.meshInfo[1].vertexCount>=4,"actual original game sprite asset generates a real TMP inline submesh");
        ActorBars.Scan(owner,false);HealthbarForegroundDepth.SetActive(true);var spriteDerived=Render("native-inline-sprite-derived");
        Check(Same(spriteOriginal,spriteDerived),"original inline sprite atlas/geometry/colour pixels survive the native Sprite derivative");
        var subs=host.GetComponentsInChildren<TMP_SubMeshUI>();string subInfo="";bool ownSprite=false;foreach(var submesh in subs){subInfo+=submesh.name+" active="+submesh.gameObject.activeInHierarchy+" material="+submesh.sharedMaterial.shader.name+"\n";ownSprite|=submesh.sharedMaterial.shader.name=="GloomhavenVR/ActorBarSprite";}File.WriteAllText(Path.Combine(evidence,"native-submesh-binding.txt"),subInfo);Check(ownSprite,"native inline sprite receives its own complete foreground-aware material");
        wall.SetActive(true);card.SetActive(true);card.transform.GetChild(0).position=new Vector3(0,0,-2);card.transform.GetChild(0).localScale=new Vector3(1.9f,1.2f,.02f);
        var covered=Render("native-inline-sprite-nearer-card");int coloured=0;foreach(var p in covered)if(p.r>40||p.g>40||p.b>40)coloured++;
        Check(coloured==0,"nearer solid card clips every native number and inline sprite pixel together");
        card.transform.GetChild(0).position=new Vector3(0,0,.8f);Check(Same(spriteDerived,Render("native-inline-sprite-farther-card")),"farther card never hides native inline sprites or font outlines");card.SetActive(false);
        var spriteClone=subs[0].sharedMaterial;int cloneCount=owner.DepthMats.Count;
        for(int update=0;update<6;update++){number.text="12<sprite=0>";Render("native-inline-update-"+update);}
        Check(subs[0].sharedMaterial==spriteClone&&owner.DepthMats.Count==cloneCount,"repeated native text reparsing reuses the exact existing font and sprite clones");
        ActorBars.Restore(owner);HealthbarForegroundDepth.SetActive(false);
        Check(owner.DepthTextRefresh.Count==0&&owner.DepthTextIds.Count==0,"restore retires every owned native pre-render listener");
        Check(number.fontSharedMaterial==full,"whole native font material binding restores exactly after through-wall mode");
        Check(camera.GetCommandBuffers(CameraEvent.BeforeForwardAlpha).Length==0&&camera.GetCommandBuffers(CameraEvent.AfterForwardAlpha).Length==0,"mode shutdown detaches both owned foreground command buffers");
        File.WriteAllText(Path.Combine(evidence,"native-font-platform.txt"),Application.unityVersion+"\n"+SystemInfo.graphicsDeviceType+"\nusesReversedZ="+SystemInfo.usesReversedZBuffer+"\nvisibleGlyphPixels="+visible+"\npartialGlyphPixels="+partial+"\nghostGlyphPixels="+ghostPixels+"\n");
        camera.targetTexture=null;VRRigDriver.HeadCamera=null;
        foreach(var go in new[]{host,cameraGo,wall,card,remote,held,hand,far})Object.DestroyImmediate(go);
        foreach(var obj in new Object[]{target,read,original,full,wallMat,ghost,duplicate,font,atlas,sprite,spriteAtlas,spriteMaterial})Object.DestroyImmediate(obj);
        return _checks;
    }
}
