using System;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Core.SteamLink;
using GloomhavenVR.WorldUI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static class WindowEdgeFixture
{
    [Serializable] private sealed class Row { public string name; public int sourcePartial,maskPartial; public float sourceCenter,maskCenter,linearWidth,cubicWidth,maxDelta,meanAlpha,nearestWidth,hybridWidth,sourceEdge,linearEdge,cubicEdge,nearestEdge,hybridEdge,nearestDelta,hybridDelta,unsharp1Width,unsharp2Width,unsharp1Edge,unsharp2Edge,unsharp1Delta,unsharp2Delta; }
    [Serializable] private sealed class Report { public bool passed;public string unity,device,backend,failure;public int checks;public string spatialMode;public List<Row> rows=new List<Row>(); }
    private static Material nearest,hybrid;private static bool unsharp;
    private static Report result=new Report();private static List<Object> owned=new List<Object>();
    private static T Keep<T>(T value)where T:Object {owned.Add(value);return value;}
    private static void Check(bool value,string why){result.checks++;if(!value)throw new Exception(why);}
    private static RenderTexture Target(int w,int h,int depth=0,int samples=1) {var r=Keep(new RenderTexture(w,h,depth,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear));r.antiAliasing=samples;Check(r.Create(),"Actual render target created");return r;}
    private static Texture2D Read(RenderTexture r) {var old=RenderTexture.active;try {RenderTexture.active=r;var t=Keep(new Texture2D(r.width,r.height,TextureFormat.RGBA32,false,true));t.ReadPixels(new Rect(0,0,r.width,r.height),0,0,false);t.Apply();return t;}finally{RenderTexture.active=old;}}
    private static float Crossing(Texture2D t,float level) {
        for(int x=1;x<t.width/2;x++){float a=t.GetPixel(x-1,t.height/2).r,b=t.GetPixel(x,t.height/2).r;if(a<=level&&b>=level&&b>a)return x-1+(level-a)/(b-a);}
        return -1;
    }
    private static float Width(Texture2D t) {float a=Crossing(t,.1f),b=Crossing(t,.9f);return a<0||b<0?-1:b-a;}
    private static Row Probe(string name,RenderTexture source,RenderTexture carrier,RenderTexture controlCarrier,SceneEyeOutput pack,Material oldAtlas,Material debug,RenderTexture sourceCopy,RenderTexture maskCopy,RenderTexture oldMaskCopy,string output,bool save) {
        Check(pack.TryRender(source,carrier,Camera.MonoOrStereoscopicEye.Left)==SceneEyeResult.Rendered,"Production atlas render");
        Graphics.Blit(source,controlCarrier,oldAtlas);Graphics.Blit(source,sourceCopy,debug,0);Graphics.Blit(carrier,maskCopy,debug,1);Graphics.Blit(controlCarrier,oldMaskCopy,debug,1);
        var s=Read(sourceCopy);var m=Read(maskCopy);var old=Read(oldMaskCopy);
        var row=new Row{name=name,sourceCenter=s.GetPixel(1024,1024).r,maskCenter=m.GetPixel(1024,1024).r,linearWidth=Width(old),cubicWidth=Width(m),sourceEdge=Crossing(s,.5f),linearEdge=Crossing(old,.5f),cubicEdge=Crossing(m,.5f)};
        for(int x=0;x<2048;x++){float a=s.GetPixel(x,1024).r,b=m.GetPixel(x,1024).r;if(a>.01f&&a<.99f)row.sourcePartial++;row.meanAlpha+=b/2048;if(b>.01f&&b<.99f)row.maskPartial++;if(x>256&&x<1792)row.maxDelta=Mathf.Max(row.maxDelta,Mathf.Abs(b-old.GetPixel(x,1024).r));}
        result.rows.Add(row);
        if(save){File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(output),name+"-source-alpha.png"),s.EncodeToPNG());File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(output),name+"-packed-alpha.png"),m.EncodeToPNG());File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(output),name+"-old-linear-alpha.png"),old.EncodeToPNG());}
        if(nearest!=null)foreach(var comparison in new[]{nearest,hybrid}) {
            Graphics.Blit(source,controlCarrier,comparison);Graphics.Blit(controlCarrier,oldMaskCopy,debug,1);var sampled=Read(oldMaskCopy);
            float delta=0;for(int x=256;x<1792;x++)delta=Mathf.Max(delta,Mathf.Abs(sampled.GetPixel(x,1024).r-old.GetPixel(x,1024).r));
            if(unsharp) {if(comparison==nearest){row.unsharp1Width=Width(sampled);row.unsharp1Edge=Crossing(sampled,.5f);row.unsharp1Delta=delta;}else{row.unsharp2Width=Width(sampled);row.unsharp2Edge=Crossing(sampled,.5f);row.unsharp2Delta=delta;}}
            else {if(comparison==nearest){row.nearestWidth=Width(sampled);row.nearestEdge=Crossing(sampled,.5f);row.nearestDelta=delta;}else{row.hybridWidth=Width(sampled);row.hybridEdge=Crossing(sampled,.5f);row.hybridDelta=delta;}}
            if(save)File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(output),name+(comparison==nearest?"-nearest-alpha.png":"-hybrid-alpha.png")),sampled.EncodeToPNG());
            Object.DestroyImmediate(sampled);
        }
        // Restore the independent old producer before the RGB identity check.
        Graphics.Blit(source,controlCarrier,oldAtlas);
        Object.DestroyImmediate(s);Object.DestroyImmediate(m);Object.DestroyImmediate(old);
        Graphics.Blit(carrier,maskCopy,debug,2);Graphics.Blit(controlCarrier,oldMaskCopy,debug,2);
        var rgb=Read(maskCopy);var rgbOld=Read(oldMaskCopy);
        foreach(int y in new[]{129,511,1024,1533,1919})foreach(int x in new[]{129,511,1024,1533,1919}) {
            Color a=rgb.GetPixel(x,y),b=rgbOld.GetPixel(x,y);
            Check(a.r==b.r&&a.g==b.g&&a.b==b.b,"RGB byte-identical to original producer");
        }
        Object.DestroyImmediate(rgb);Object.DestroyImmediate(rgbOld);return row;
    }
    public static void Start()
    {
        string[] args=Environment.GetCommandLineArgs();string output=args[Array.IndexOf(args,"-windowResult")+1];int code=1;
        AssetBundle bank=null;SceneEyeCoverage coverage=null;SceneEyeOutput pack=null;
        try {
            result.unity=Application.unityVersion;result.device=SystemInfo.graphicsDeviceName;result.backend=SystemInfo.graphicsDeviceType.ToString();
            Check(Application.unityVersion=="2021.3.5f1"&&QualitySettings.activeColorSpace==ColorSpace.Gamma&&result.backend!="Null","Actual Gamma GPU");
            bank=AssetBundle.LoadFromFile(args[Array.IndexOf(args,"-windowBank")+1]);Check(bank,"Actual shipping bank");
            Shader atlas=null,cover=null;foreach(var s in bank.LoadAllAssets<Shader>()){if(s.name==SceneAtlasProfile.ShaderName)atlas=s;if(s.name==SceneEyeCoverage.ShaderName)cover=s;}
            Check(atlas&&cover,"Actual bank atlas and coverage");pack=new SceneEyeOutput(atlas);coverage=new SceneEyeCoverage(cover);
            var go=Keep(new GameObject("Owned head"));var c=go.AddComponent<Camera>();c.enabled=false;c.orthographic=true;c.orthographicSize=1;c.transform.position=new Vector3(0,0,-4);c.clearFlags=CameraClearFlags.SolidColor;c.backgroundColor=Color.clear;c.allowHDR=false;c.renderingPath=RenderingPath.Forward;c.cullingMask=1<<30;c.nearClipPlane=.1f;c.farClipPlane=10;c.targetTexture=Target(2048,2048,24);
            Check(coverage.Arm(c,1),"Actual depth coverage arm");var carrier=Target(3408,3408);var controlCarrier=Target(3408,3408);var oldAtlas=Keep(new Material(Shader.Find("SceneWindowEdge/LinearControl")));var debug=Keep(new Material(Shader.Find("SceneWindowEdge/Probe")));var sourceCopy=Target(2048,2048);var maskCopy=Target(2048,2048);var oldMaskCopy=Target(2048,2048);
            var plate=Keep(GameObject.CreatePrimitive(PrimitiveType.Quad));plate.layer=30;plate.transform.localScale=new Vector3(1.131f,1.221f,1);var material=Keep(new Material(Shader.Find("GloomhavenVR/Overlay")));material.color=new Color(.12f,.11f,.1f,1);material.SetInt("_SrcBlend",1);material.SetInt("_DstBlend",0);material.SetInt("_ZWrite",1);material.renderQueue=2998;plate.GetComponent<Renderer>().sharedMaterial=material;
            var ui=Keep(new GameObject("Actual window UI"));ui.layer=30;var canvas=ui.AddComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.worldCamera=c;ui.transform.localScale=Vector3.one*.001f;ui.transform.position=new Vector3(0,0,-.01f);var rt=ui.GetComponent<RectTransform>();rt.sizeDelta=new Vector2(1100,1200);
            var imageGo=Keep(new GameObject("Captured window RawImage",typeof(RectTransform)));imageGo.layer=30;imageGo.transform.SetParent(ui.transform,false);var image=imageGo.AddComponent<RawImage>();image.rectTransform.sizeDelta=new Vector2(1100,1200);
            var texture=Keep(new Texture2D(64,64,TextureFormat.RGBA32,false,true));var pix=new Color[64*64];for(int y=0;y<64;y++)for(int x=0;x<64;x++){float a=Mathf.Clamp01(Mathf.Min(Mathf.Min(x,y),Mathf.Min(63-x,63-y))/4f);pix[y*64+x]=new Color(.25f,.3f,.35f,a);}texture.SetPixels(pix);texture.Apply();image.texture=texture;Canvas.ForceUpdateCanvases();
            bool clipOnly=Array.IndexOf(args,"-windowClipOnly")>=0;
            bool edgeOnly=Array.IndexOf(args,"-windowEdgeOnly")>=0;
            bool compareOnly=Array.IndexOf(args,"-windowCompareOnly")>=0;unsharp=Array.IndexOf(args,"-windowUnsharpOnly")>=0;result.spatialMode=unsharp?"unsharp-k1-k2":"nearest-hybrid";
            if(compareOnly||unsharp) {
                nearest=Keep(new Material(Shader.Find("SceneWindowEdge/SpatialComparison")));nearest.SetFloat("_Kernel",unsharp?3:1);hybrid=Keep(new Material(nearest));hybrid.SetFloat("_Kernel",unsharp?4:2);
                c.targetTexture=Target(2048,2048,24,4);ui.SetActive(false);
                foreach(int angle in new[]{0,13})for(int phase=0;phase<16;phase++) {
                    plate.SetActive(true);plate.transform.rotation=Quaternion.Euler(0,0,angle);plate.transform.position=new Vector3(phase*.0001220703125f,0,0);
                    c.Render();Probe("moving-"+angle+"-"+phase,c.targetTexture,carrier,controlCarrier,pack,oldAtlas,debug,sourceCopy,maskCopy,oldMaskCopy,output,angle==0&&phase==0);
                }
                plate.SetActive(false);ui.SetActive(true);image.rectTransform.sizeDelta=new Vector2(2000,2000);
                foreach(float alpha in new[]{.1f,.25f,.5f,.75f}) {
                    for(int i=0;i<pix.Length;i++)pix[i]=new Color(.25f,.3f,.35f,alpha);texture.SetPixels(pix);texture.Apply();c.Render();
                    var row=Probe("compare-glass-"+alpha,c.targetTexture,carrier,controlCarrier,pack,oldAtlas,debug,sourceCopy,maskCopy,oldMaskCopy,output,false);
                    Check((unsharp?row.unsharp1Delta:row.nearestDelta)<2.1f/255&&(unsharp?row.unsharp2Delta:row.hybridDelta)<2.1f/255,"Spatial controls retain partial glass");
                }
                for(int y=0;y<64;y++)for(int x=0;x<64;x++)pix[y*64+x]=new Color(.25f,.3f,.35f,.1f+.8f*x/63);texture.SetPixels(pix);texture.Apply();c.Render();
                var gradient=Probe("compare-gradient",c.targetTexture,carrier,controlCarrier,pack,oldAtlas,debug,sourceCopy,maskCopy,oldMaskCopy,output,true);
                Check((unsharp?gradient.unsharp1Delta:gradient.nearestDelta)<2.1f/255&&(unsharp?gradient.unsharp2Delta:gradient.hybridDelta)<2.1f/255,"Spatial controls retain smooth gradient");
            }
            else if(clipOnly) {
                plate.SetActive(false);image.rectTransform.sizeDelta=new Vector2(2000,2000);
                for(int i=0;i<pix.Length;i++)pix[i]=new Color(.25f,.3f,.35f,.65f);texture.SetPixels(pix);texture.Apply();
                var clipGo=Keep(new GameObject("Actual native RectMask2D",typeof(RectTransform)));clipGo.layer=30;clipGo.transform.SetParent(ui.transform,false);var clip=clipGo.AddComponent<RectMask2D>();var clipRect=clipGo.GetComponent<RectTransform>();clipRect.sizeDelta=new Vector2(600,900);image.transform.SetParent(clipGo.transform,false);
                foreach(int softness in new[]{0,6})foreach(float fade in new[]{1f,.4f}) {
                    clip.softness=new Vector2Int(softness,softness);image.color=new Color(1,1,1,fade);Canvas.ForceUpdateCanvases();c.Render();
                    var row=Probe("rectmask-soft"+softness+"-fade"+fade,c.targetTexture,carrier,controlCarrier,pack,oldAtlas,debug,sourceCopy,maskCopy,oldMaskCopy,output,softness==6&&fade==1);
                    Check(Mathf.Abs(row.maskCenter-.65f*fade)<2.1f/255,"Real rectangular clip preserves interior fade");
                    Graphics.Blit(carrier,maskCopy,debug,1);var clipped=Read(maskCopy);Check(clipped.GetPixel(600,1024).r==0&&clipped.GetPixel(1450,1024).r==0,"No opacity outside actual clipped silhouette");Object.DestroyImmediate(clipped);
                    Check(row.maskPartial>0,"Clipped partial opacity remains partial");
                }
            }
            else {
            float oldWidths=0,newWidths=0;
            // Actual physical opaque backing, UI/Default above it, then the shipping
            // producer shader. Subpixel translations and non-axis-aligned geometry
            // retain real raster/MSAA samples; the kernel never thresholds alpha.
            for(int pose=0;pose<6;pose++) {
                plate.SetActive(true);plate.transform.rotation=Quaternion.Euler(0,pose>=3?23:0,pose%3==2?13:0);
                plate.transform.position=new Vector3((pose%3-1)*.00037f,0,0);
                if(pose==3){c.targetTexture=Target(2048,2048,24,4);Check(c.targetTexture.antiAliasing==4,"Actual4x MSAA source");}
                ui.transform.rotation=plate.transform.rotation;ui.transform.position=plate.transform.position-plate.transform.forward*.01f;
                c.Render();var row=Probe("opaque-window-"+pose,c.targetTexture,carrier,controlCarrier,pack,oldAtlas,debug,sourceCopy,maskCopy,oldMaskCopy,output,pose==0);
                Check(row.sourceCenter>.99f,"Real UI cannot lower opaque backing alpha");
                Check(row.sourcePartial<=4,"Opaque backing keeps normal raster edge coverage");
                oldWidths+=row.linearWidth;newWidths+=row.cubicWidth;
            }
            Check(newWidths<oldWidths-.05f,"Sharper aggregate 10-90 edge width versus old linear kernel: "+newWidths+" vs "+oldWidths);
            if(!edgeOnly) {
            plate.SetActive(false);ui.transform.rotation=Quaternion.identity;ui.transform.position=new Vector3(0,0,-.01f);image.rectTransform.sizeDelta=new Vector2(2000,2000);
            foreach(float alpha in new[]{0f,.1f,.25f,.5f,.75f,1f}) {
                for(int i=0;i<pix.Length;i++)pix[i]=new Color(.25f,.3f,.35f,alpha);texture.SetPixels(pix);texture.Apply();
                c.Render();var row=Probe("constant-glass-"+alpha,c.targetTexture,carrier,controlCarrier,pack,oldAtlas,debug,sourceCopy,maskCopy,oldMaskCopy,output,false);
                Check(Mathf.Abs(row.maskCenter-alpha)<2.1f/255,"Constant glass alpha preserved "+alpha);
                Check(row.maxDelta<2.1f/255,"Constant opacity interior unchanged");
            }
            for(int y=0;y<64;y++)for(int x=0;x<64;x++)pix[y*64+x]=new Color(.25f,.3f,.35f,.1f+.8f*x/63);
            texture.SetPixels(pix);texture.Apply();
            foreach(float fade in new[]{1f,.65f,.25f,0f}) {
                image.color=new Color(1,1,1,fade);Canvas.ForceUpdateCanvases();c.Render();
                var row=Probe("gradient-fade-"+fade,c.targetTexture,carrier,controlCarrier,pack,oldAtlas,debug,sourceCopy,maskCopy,oldMaskCopy,output,fade==1f);
                Check(row.maxDelta<2.1f/255,"Smooth gradient/fade stays continuous, no opacity threshold");
            }
            // The actual owner uses a spatial mesh/vertex-alpha erosion, not an
            // all-or-nothing window rectangle. Exercise that production helper.
            ui.SetActive(false);plate.SetActive(true);plate.transform.position=Vector3.zero;plate.transform.rotation=Quaternion.identity;
            Shader fadingShader=null;foreach(var candidate in bank.LoadAllAssets<Shader>())if(candidate.name=="GloomhavenVR/Overlay.SceneCoverage")fadingShader=candidate;
            Check(fadingShader,"Actual shipping fade coverage variant");var fadeMaterial=Keep(new Material(material));fadeMaterial.shader=fadingShader;fadeMaterial.SetInt("_SrcBlend",5);fadeMaterial.SetInt("_DstBlend",10);fadeMaterial.SetInt("_SceneCoverageSrcAlpha",1);fadeMaterial.SetInt("_SceneCoverageDstAlpha",10);fadeMaterial.SetInt("_ZWrite",0);
            using(var materialise=new MrBackingMaterialise()) {
                float lastMean=1;
                foreach(float progress in new[]{.15f,.4f,.7f,1f}) {
                    materialise.Apply(plate.transform,new Rect(-.5f,-.5f,1,1),new Rect(-.5f,-.5f,1,1),progress,fadeMaterial);c.Render();
                    var row=Probe("materialise-"+progress,c.targetTexture,carrier,controlCarrier,pack,oldAtlas,debug,sourceCopy,maskCopy,oldMaskCopy,output,progress==.4f);
                    Check(row.meanAlpha<=lastMean+.005f,"Production materialisation remains monotone");lastMean=row.meanAlpha;
                    Check(row.maxDelta<.09f,"Erosion keeps continuous coverage, bounded spatial redistribution");
                    if(progress==1)Check(row.meanAlpha==0,"Fully vanished window stays transparent");
                }
            }
            }
            }
            result.passed=true;code=0;
        }catch(Exception e){result.failure=e.ToString();Debug.LogException(e);}finally{if(coverage!=null)coverage.Dispose();if(pack!=null)pack.Dispose();for(int i=owned.Count-1;i>=0;i--)if(owned[i])Object.DestroyImmediate(owned[i]);if(bank)bank.Unload(true);File.WriteAllText(output,JsonUtility.ToJson(result,true)+"\n");}EditorApplication.Exit(code);
    }
}
