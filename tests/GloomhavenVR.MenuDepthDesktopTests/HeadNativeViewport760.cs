using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static partial class MenuDepthDesktopProgram
{
    public static IEnumerator HeadNativeViewport760(string output,Action<int> complete)
    {
        _count=0;DesktopEyeInput.Available=true;
        var handle=(Action<CanvasScaler>)Delegate.CreateDelegate(typeof(Action<CanvasScaler>),typeof(CanvasScaler).GetMethod("Handle",BindingFlags.Instance|BindingFlags.NonPublic)!);
            using(var scene=new Scene())using(var font=new NativeFont742(output))
            {
                foreach(var root in new[]{scene.RootA,scene.RootB})foreach(var g in root.GetComponentsInChildren<Graphic>(true))g.enabled=false;
                scene.HeadTarget.Release();scene.HeadTarget.width=1600;scene.HeadTarget.height=1000;scene.HeadTarget.Create();
                scene.Head.orthographic=false;scene.Head.fieldOfView=60;scene.Head.aspect=1.6f;scene.Head.nearClipPlane=.01f;scene.Head.farClipPlane=200;
                scene.Screen.localScale=new Vector3(1.2f,.675f,1);
                var rootCanvas=scene.SpatialGraphic.canvas.rootCanvas;
                var rootRect=(RectTransform)rootCanvas.transform;
                var scaler=rootCanvas.gameObject.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);
                var textures=new List<Texture2D>();var sprites=new List<Sprite>();
                string inputs=Path.Combine(Path.GetDirectoryName(output)!,"native-creation-inputs");
                var native=JsonUtility.FromJson<NativeTree760>(File.ReadAllText(Path.Combine(inputs,"creation.json")));
                var target=ImportTree760(native,rootRect,inputs,font,textures,sprites);
                foreach(var rect in target.GetComponentsInChildren<RectTransform>(true))if(rect.name=="New Party display"||rect.name=="Campaign Adventure Party Assembly Variant"||rect.name=="UI Adventure Assembly Character Creator"||rect.name=="UI Character Creator Window"||rect.name=="UI Character Creator Personal Quest Step"||rect.name.StartsWith("UI Character Create Personal Quest Choice"))rect.gameObject.SetActive(true);
                foreach(var image in target.GetComponentsInChildren<Image>(true))image.gameObject.layer=5;
                foreach(var text in target.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))text.gameObject.layer=5;
                var left=FindRect760(target,"UI Character Create Personal Quest Choice (1)");var right=FindRect760(target,"UI Character Create Personal Quest Choice");
                var marker=right.GetComponentInChildren<Image>();marker.gameObject.AddComponent<MenuDepthGraphic>().Configure(marker,null,.01f);
                foreach(var desktop in new[]{new Vector2Int(1920,1080),new Vector2Int(3440,1440),new Vector2Int(1280,720),new Vector2Int(1920,1080)})
                {
                    Vector2Int raster=NativeUiViewport.Raster(desktop.x,desktop.y);
                    Check(raster.x>=desktop.x&&raster.y>=desktop.y,"canonical viewport never reduces either previous capture axis");
                    scene.Ui.Release();scene.Ui.width=raster.x;scene.Ui.height=raster.y;scene.Ui.Create();
                    scene.Background.Release();scene.Background.width=raster.x;scene.Background.height=raster.y;scene.Background.Create();
                    scene.OriginalA.targetTexture=null;scene.OriginalA.enabled=true;scene.OriginalB.enabled=true;
                    scene.Flat.BindPixelOwner760();scene.Flat.Begin();
                    Check(FlatScreen.OwnsNativeUiPixels(rootCanvas,scene.OriginalA),"only the current captured native canvas uses its owned pixel coordinates");
                    Check(!FlatScreen.OwnsNativeUiPixels(rootCanvas,scene.OriginalB),"a foreign event camera cannot borrow native pixel ownership");
                    foreach(var mode in new[]{CanvasScaler.ScreenMatchMode.MatchWidthOrHeight,CanvasScaler.ScreenMatchMode.Expand,CanvasScaler.ScreenMatchMode.Shrink})
                    foreach(float match in new[]{0f,.5f,1f})
                    {
                        scaler.screenMatchMode=mode;scaler.matchWidthOrHeight=match;scaler.referencePixelsPerUnit=137f;
                        handle(scaler);Canvas.ForceUpdateCanvases();yield return null;
                        Check((rootRect.rect.size-new Vector2(1920,1080)).sqrMagnitude<.1f,"native captured Canvas keeps its authored frame across desktop aspect and every match mode");
                        Check(Mathf.Abs(rootCanvas.referencePixelsPerUnit-137f)<.001f,"native scaler retains referencePixelsPerUnit side effect");
                    }
                    scene.ModelCamera.Render();scene.OriginalA.Render();scene.OriginalB.Render();scene.Eyes(Vector3.zero);scene.Render(false);
                    Check(scene.Flat.Right!=null,"actual flat-screen coordinator captures the complete canonical native window");
                    foreach(bool eye in new[]{false,true})
                    {
                        scene.Render(eye);var pixels=Read(scene.HeadTarget,Path.Combine(output,"native-"+desktop.x+"x"+desktop.y+"-"+(eye?"right":"left")+".png"));
                        foreach(var quest in new[]{left,right})
                        {
                            Vector3 source=scene.OriginalA.WorldToScreenPoint(quest.TransformPoint(quest.rect.center));Vector2 p=NativePixelInHead730(scene,source,eye);
                            int bright=0;for(int y=-18;y<=18;y++)for(int x=-18;x<=18;x++){Color c=pixels.GetPixel((int)p.x+x,(int)p.y+y);if(c.r>.1f&&c.g>.1f)bright++;}
                            Check(bright>200,"both native quest panes remain present in final unconverted head pixels");
                        }
                        Object.DestroyImmediate(pixels);
                    }
                    scene.Flat.End();Check(!FlatScreen.OwnsNativeUiPixels(rootCanvas,scene.OriginalA),"native pixel ownership ends immediately with the real capture release");yield return null;float restored=rootCanvas.scaleFactor;handle(scaler);
                    Check(Mathf.Abs(rootCanvas.scaleFactor-restored)<.0001f,"the next original scaler update restores its current-display metric and coherent cache");
                }
                foreach(var sprite in sprites)Object.DestroyImmediate(sprite);foreach(var texture in textures)Object.DestroyImmediate(texture);
            }
        complete(_count);
    }
}
