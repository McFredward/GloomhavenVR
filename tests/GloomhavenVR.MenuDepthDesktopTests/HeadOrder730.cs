using System;
using System.Collections;
using System.IO;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static partial class MenuDepthDesktopProgram
{
    public static IEnumerator HeadOrder730(string output,Action<int> complete)
    {
        _count=0;DesktopEyeInput.Available=true;
        using(var scene=new Scene())
        {
            scene.HideSpatial730();MenuDepthCharacters.Shutdown();scene.Screen.GetComponent<Renderer>().enabled=false;
            scene.Head.orthographic=false;scene.Head.fieldOfView=60;scene.HeadTarget.Release();scene.HeadTarget.width=128;scene.HeadTarget.height=96;scene.HeadTarget.Create();scene.Eyes(Vector3.zero);
            var first=new GameObject("mod photograph first",typeof(RectTransform),typeof(Canvas),typeof(CanvasRenderer),typeof(RawImage));first.layer=27;
            var second=new GameObject("mod photograph second",typeof(RectTransform),typeof(Canvas),typeof(CanvasRenderer),typeof(RawImage));second.layer=27;
            foreach(var go in new[]{first,second})
            {
                var rect=(RectTransform)go.transform;rect.sizeDelta=new Vector2(200,200);rect.position=new Vector3(0,0,2);rect.localScale=Vector3.one*.005f;
                go.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;go.GetComponent<RawImage>().texture=Texture2D.whiteTexture;
            }
            var aCanvas=first.GetComponent<Canvas>();var bCanvas=second.GetComponent<Canvas>();aCanvas.sortingOrder=1;bCanvas.sortingOrder=2;
            var red=new RenderTexture(128,96,24,RenderTextureFormat.ARGB32);var green=new RenderTexture(128,96,24,RenderTextureFormat.ARGB32);red.Create();green.Create();
            RenderTexture? previous=RenderTexture.active;
            RenderTexture.active=red;GL.Clear(true,true,Color.red);RenderTexture.active=green;GL.Clear(true,true,Color.green);RenderTexture.active=previous;
            var redMaterial=MenuDepthDisplay.CreateDisplayMaterial(red,red);var greenMaterial=MenuDepthDisplay.CreateDisplayMaterial(green,green);
            var left=scene.Head.GetStereoViewMatrix(Camera.StereoscopicEye.Left).inverse.MultiplyPoint3x4(Vector3.zero);
            var right=scene.Head.GetStereoViewMatrix(Camera.StereoscopicEye.Right).inverse.MultiplyPoint3x4(Vector3.zero);
            var leftEye=new MenuDepthEye(false,left,scene.Head);var rightEye=new MenuDepthEye(true,right,scene.Head);
            MenuDepthDisplay.SetHeadCapture(redMaterial,red,red,leftEye,rightEye,new Vector3(0,0,2),Vector3.forward);
            MenuDepthDisplay.SetHeadCapture(greenMaterial,green,green,leftEye,rightEye,new Vector3(0,0,2),Vector3.forward);
            var a=new MenuDepthHeadCarrier(first.GetComponent<RawImage>(),redMaterial);var b=new MenuDepthHeadCarrier(second.GetComponent<RawImage>(),greenMaterial);
            Camera.CameraCallback begin=c=>{if(c==scene.Head){a.Draw(c);b.Draw(c);}};
            Camera.CameraCallback end=c=>{if(c==scene.Head){a.EndDraw();b.EndDraw();}};
            Camera.onPreCull+=begin;Camera.onPostRender+=end;
            try
            {
                Check(a.HasHeadPresentation&&b.HasHeadPresentation&&!aCanvas.enabled&&!bCanvas.enabled,
                    "head presentation owns only the disabled private photograph Canvases");
                Check(first.GetComponent<RawImage>().enabled&&second.GetComponent<RawImage>().enabled,
                    "private photograph Canvas retirement leaves Graphic eligibility unchanged");
                Canvas.ForceUpdateCanvases();scene.Render(false);
                Texture2D initial=Read(scene.HeadTarget,Path.Combine(output,"native-order-second-front.png"));
                Color firstPixel=initial.GetPixel(64,48);Object.DestroyImmediate(initial);
                Check(firstPixel.g>.95f&&firstPixel.r<.02f,"actual final head pixels initially show the native frontmost window");
                aCanvas.sortingOrder=3; // Same original order follower's focus update, while photo Canvas is retired.
                Canvas.ForceUpdateCanvases();scene.Render(false);
                Texture2D focused=Read(scene.HeadTarget,Path.Combine(output,"native-order-first-focused.png"));
                Color focusedPixel=focused.GetPixel(64,48);Object.DestroyImmediate(focused);
                Check(focusedPixel.r>.95f&&focusedPixel.g<.02f,
                    "actual final head pixels follow native window focus sorting changes without reopening");
                a.EndDraw();b.EndDraw();
                Check(!aCanvas.enabled&&!bCanvas.enabled,"head completion cannot resurrect a cached private mono photograph");
                bCanvas.enabled=true; // A foreign later writer remains authoritative on disposal.
                a.Dispose();b.Dispose();
                Check(aCanvas.enabled&&bCanvas.enabled&&!a.HasHeadPresentation&&!b.HasHeadPresentation,
                    "retiring head presentation restores only the owned private Canvas state");
            }
            finally
            {
                Camera.onPreCull-=begin;Camera.onPostRender-=end;a.Dispose();b.Dispose();
                MenuDepthDisplay.ReleaseDisplayMaterial(redMaterial);MenuDepthDisplay.ReleaseDisplayMaterial(greenMaterial);
                red.Release();green.Release();Object.DestroyImmediate(red);Object.DestroyImmediate(green);Object.DestroyImmediate(first);Object.DestroyImmediate(second);
            }
        }
        complete(_count);yield break;
    }
}
