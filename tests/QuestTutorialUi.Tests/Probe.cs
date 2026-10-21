using System;
using System.IO;
using GloomhavenVR.WorldUI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class QuestTutorialUiProbe
{
    [Serializable] sealed class Result { public bool passed, hardwareVerified; public int baselineInside, baselineOutside, rectangleInside, rectangleOutside, assertions, unchangedInteriorPixels, defectControls; public string failure; }
    static int assertions;
    static void Check(bool value,string message){assertions++;if(!value)throw new Exception(message);}
    static int Count(Color32[] pixels, bool outside, float low, float high)
    {
        int count=0;
        for(int y=0;y<256;y++) for(int x=0;x<256;x++)
        { Color32 p=pixels[y*256+x]; if((y<low||y>=high)==outside&&p.r>24&&p.r>p.g*2&&p.r>p.b*2)count++; }
        return count;
    }
    static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 position)
    {
        var go=new GameObject(name,typeof(RectTransform)); var rect=(RectTransform)go.transform;
        rect.SetParent(parent,false); rect.sizeDelta=size; rect.anchoredPosition=position; return rect;
    }
    static Color32[] Pixels(Camera camera, RenderTexture target)
    {
        Canvas.ForceUpdateCanvases();camera.Render();var cpu=new Texture2D(256,256,TextureFormat.RGBA32,false,true);
        var previous=RenderTexture.active;
        try { RenderTexture.active=target;cpu.ReadPixels(new Rect(0,0,256,256),0,0);cpu.Apply();return cpu.GetPixels32(); }
        finally { RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(cpu); }
    }
    public static void Run()
    {
        var result=new Result();int code=1;
        try
        {
            var camera=new GameObject("native-ui-camera").AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=100;
            camera.transform.position=new Vector3(0,0,-10);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
            var target=new RenderTexture(256,256,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);target.Create();camera.targetTexture=target;
            var root=new GameObject("native-tutorial-canvas",typeof(RectTransform),typeof(Canvas));var canvas=root.GetComponent<Canvas>();
            canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            var viewport=Rect("native-tutorial-viewport",root.transform,new Vector2(160,60),Vector2.zero);
            var maskImage=viewport.gameObject.AddComponent<Image>();maskImage.raycastTarget=true;
            var nativeMask=viewport.gameObject.AddComponent<Mask>();nativeMask.showMaskGraphic=false;
            var font=Resources.Load<TMP_FontAsset>("fonts & materials/Sarala-Regular SDF");
            var material=Resources.Load<Material>("fonts & materials/Sarala-Regular-Shadow SDF");
            if(font==null||material==null||material.shader==null||!material.shader.isSupported)throw new Exception("Actual native tutorial font/material did not import");
            for(int i=-1;i<=1;i++)
            {
                var text=Rect("native-description-"+i,viewport,new Vector2(180,32),new Vector2(0,i*40)).gameObject.AddComponent<TextMeshProUGUI>();
                text.font=font;text.fontSharedMaterial=material;text.fontSize=24;text.color=Color.red;text.alignment=TextAlignmentOptions.Center;
                text.text="Tutorial clipping";text.ForceMeshUpdate(true);
            }
            Canvas.ForceUpdateCanvases();
            var corners=new Vector3[4];viewport.GetWorldCorners(corners);
            float low=camera.WorldToScreenPoint(corners[0]).y,high=camera.WorldToScreenPoint(corners[2]).y;
            Color32[] before=Pixels(camera,target);
            result.baselineInside=Count(before,false,low,high);result.baselineOutside=Count(before,true,low,high);
            Check(!QuestTutorialViewportMask.Apply(viewport,false)&&viewport.GetComponent<RectMask2D>()==null,"Desktop tutorial retains its original mask only");
            nativeMask.enabled=false;Check(!QuestTutorialViewportMask.Apply(viewport,true),"Disabled native mask is not replaced");nativeMask.enabled=true;
            nativeMask.showMaskGraphic=true;Check(!QuestTutorialViewportMask.Apply(viewport,true),"Visible mask presentation is not changed");nativeMask.showMaskGraphic=false;
            maskImage.fillCenter=false;Check(!QuestTutorialViewportMask.Apply(viewport,true),"Partial native mask is not replaced");maskImage.fillCenter=true;
            var shapeTexture=new Texture2D(2,2);var shape=Sprite.Create(shapeTexture,new Rect(0,0,2,2),Vector2.one*.5f);
            maskImage.sprite=shape;Check(!QuestTutorialViewportMask.Apply(viewport,true),"Shaped native mask is not replaced");maskImage.sprite=null;
            Material nativeMaterial=material;Image.Type nativeType=maskImage.type;
            Check(QuestTutorialViewportMask.Apply(viewport,true),"Actual Quest tutorial rectangle repair is admitted");
            var rectangle=viewport.GetComponent<RectMask2D>();
            Check(!QuestTutorialViewportMask.Apply(viewport,true)&&viewport.GetComponents<RectMask2D>().Length==1,"Repeated tutorial initialization does not duplicate clipping");
            Check(nativeMask.enabled&&!nativeMask.showMaskGraphic&&material==nativeMaterial&&maskImage.type==nativeType,"Original mask and native font presentation remain intact");
            Canvas.ForceUpdateCanvases();rectangle.PerformClipping();
            Color32[] after=Pixels(camera,target);
            result.rectangleInside=Count(after,false,low,high);result.rectangleOutside=Count(after,true,low,high);
            if(result.baselineInside<50||result.baselineOutside<50)throw new Exception("Native exported TMP stencil defect was not reproduced");
            if(result.rectangleInside<50||result.rectangleOutside!=0)throw new Exception("Native TMP rectangle clipping did not preserve inner text and remove outside text");
            Vector3 lowCorner=camera.WorldToScreenPoint(corners[0]),highCorner=camera.WorldToScreenPoint(corners[2]);
            for(int y=(int)low+2;y<(int)high-2;y++)for(int x=(int)lowCorner.x+2;x<(int)highCorner.x-2;x++)
            {
                int index=y*256+x;Color32 a=before[index],b=after[index];
                Check(Math.Abs(a.r-b.r)<=1&&Math.Abs(a.g-b.g)<=1&&Math.Abs(a.b-b.b)<=1&&Math.Abs(a.a-b.a)<=1,"Native interior pixels changed under rectangular clipping");
                result.unchangedInteriorPixels++;
            }
            rectangle.enabled=false;Color32[] broken=Pixels(camera,target);
            Check(Count(broken,true,low,high)>50,"Disabling only the repair must restore the native text leak");result.defectControls++;
            rectangle.enabled=true;Canvas.ForceUpdateCanvases();rectangle.PerformClipping();
            Check(Count(Pixels(camera,target),true,low,high)==0,"Reenabled repair must clip native text again");
            foreach(var text in viewport.GetComponentsInChildren<TextMeshProUGUI>())text.rectTransform.anchoredPosition+=new Vector2(0,11);
            Canvas.ForceUpdateCanvases();rectangle.PerformClipping();
            Check(Count(Pixels(camera,target),true,low,high)==0,"Moving native scroll content retains its authored clip boundary");
            result.assertions=assertions;result.passed=true;code=0;
        }
        catch(Exception e){result.failure=e.ToString();UnityEngine.Debug.LogException(e);}
        Directory.CreateDirectory("ProbeOutput");File.WriteAllText("ProbeOutput/results.json",JsonUtility.ToJson(result,true));EditorApplication.Exit(code);
    }
}
