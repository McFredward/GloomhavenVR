using System;
using System.IO;
using System.Reflection;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;

// The handoff and restore bodies execute verbatim from the menu-only production
// conversion path. Parent creation, native adoption's head-camera assignment and
// reveal visibility are explicit boundaries. Actual Unity Canvas/CanvasScaler and
// Camera.Render provide the adapter and final-pixel witness; no native controller
// callback, OpenXR provider or headset appearance is certified by this fixture.
public static class NativeMenuCanvasProgram
{
    private static int count;
    private static void Check(bool condition, string message)
    { count++; if (!condition) throw new InvalidOperationException(message); }

    private static RectTransform Rect(string name, Transform? parent = null)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(parent, false); rect.sizeDelta = new Vector2(1920,1080);
        return rect;
    }

    // Exact native level4 UIMapEscMenu CanvasScaler fields: mode1, reference
    // 1920x1080, Expand1, match .5, reference pixels100, scale/dynamic pixels1.
    private static CanvasScaler NativeScaler(GameObject owner)
    {
        var scaler = owner.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920,1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        scaler.matchWidthOrHeight = .5f; scaler.referencePixelsPerUnit = 100f;
        scaler.scaleFactor = scaler.dynamicPixelsPerUnit = 1f;
        return scaler;
    }

    private static void NativeLayout(CanvasScaler scaler)
    {
        // The coordinator keeps the menu Canvas render-hidden across native
        // Updates. Invoke the real installed uGUI Handle, not a copied formula,
        // to exercise that next native Update without sleeping a synthetic frame.
        typeof(CanvasScaler).GetMethod("Handle", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(scaler, null);
        Canvas.ForceUpdateCanvases();
    }

    private static Color Capture(Camera head, Vector3 point, string label)
    {
        head.Render(); RenderTexture? previous = RenderTexture.active;
        var read = new Texture2D(head.targetTexture.width, head.targetTexture.height, TextureFormat.RGBA32, false);
        try
        {
            RenderTexture.active = head.targetTexture;
            read.ReadPixels(new Rect(0,0,read.width,read.height),0,0); read.Apply();
            Vector3 pixel = head.WorldToScreenPoint(point);
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath,"../../captures"));
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder,typeof(NativeMenuCanvasProgram).Assembly.GetName().Name+"-"+label+".png"),read.EncodeToPNG());
            return read.GetPixel(Mathf.Clamp((int)pixel.x,0,read.width-1),Mathf.Clamp((int)pixel.y,0,read.height-1));
        }
        finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(read); }
    }

    public static int Run()
    {
        count=0;
        Camera native = new GameObject("NativeMenuFixture.UI Camera").AddComponent<Camera>();
        native.orthographic=true; native.orthographicSize=100f;
        native.nearClipPlane=-100f; native.farClipPlane=1000f;
        var nativeTarget=new RenderTexture(1920,1080,24);nativeTarget.Create();native.targetTexture=nativeTarget;
        Camera head=new GameObject("NativeMenuFixture.Head").AddComponent<Camera>();
        head.transform.position=new Vector3(0,1,0);head.fieldOfView=90;head.nearClipPlane=.01f;
        head.clearFlags=CameraClearFlags.SolidColor;head.backgroundColor=Color.black;
        var eye=new RenderTexture(128,128,24);eye.Create();head.targetTexture=eye;
        RectTransform host=Rect("NativeMenuFixture.WorldHost");
        Canvas world=host.gameObject.AddComponent<Canvas>();world.renderMode=RenderMode.WorldSpace;world.worldCamera=head;
        host.position=new Vector3(0,1,2);host.localScale=Vector3.one*.001f;
        RectTransform target=Rect("NativeMenuFixture.UI Map Esc Menu");
        Canvas menu=target.gameObject.AddComponent<Canvas>();menu.renderMode=RenderMode.ScreenSpaceCamera;
        menu.worldCamera=native;menu.planeDistance=100f;menu.sortingOrder=1200;
        CanvasScaler scaler=NativeScaler(target.gameObject);
        var ink=Rect("NativeMenuFixture.MenuInk",target).gameObject.AddComponent<Image>();
        ink.color=Color.red;ink.rectTransform.sizeDelta=new Vector2(368,1080);
        ink.rectTransform.anchoredPosition=new Vector2(-760,0);
        var panel=new ConvertedPanel {Target=target,HostRect=host,HostGo=host.gameObject};
        try
        {
            NativeLayout(scaler);native.Render();
            Check(menu.isRootCanvas && menu.renderMode==RenderMode.ScreenSpaceCamera
                && Vector2.Distance(target.rect.size,new Vector2(1920,1080))<.02f,
                "native menu fixture retains authored camera/scaler dimensions: root="+menu.isRootCanvas+", mode="+menu.renderMode+", rect="+target.rect);
            for(int opening=0;opening<3;opening++)
            {
                CanvasConversion.MenuFrame(panel,target,host,new Vector2(1920,1080),true);
                menu.enabled=false;world.enabled=false;menu.worldCamera=head;
                for(int pose=0;pose<3;pose++)
                {
                    head.transform.position=new Vector3(pose*.04f,1,0);
                    NativeLayout(scaler);head.Render();
                }
                menu.enabled=true;world.enabled=true;Canvas.ForceUpdateCanvases();
                Vector3 sample=host.TransformPoint(new Vector3(-760,0,0));
                Color first=Capture(head,sample,"opening"+opening+"-first");
                Check(first.r>.8f && first.g<.1f && first.b<.1f,
                    "first menu head pixels already use the final native window frame");
                Check(Vector2.Distance(target.rect.size,new Vector2(1920,1080))<.02f && target.localScale==Vector3.one
                    && target.localPosition.sqrMagnitude<.000001f && Quaternion.Angle(target.localRotation,Quaternion.identity)<.001f,
                    "hidden native scaler updates cannot reproject the menu onto the head grid");
                NativeLayout(scaler);Color settled=Capture(head,sample,"opening"+opening+"-settled");
                Check(Mathf.Abs(first.r-settled.r)<.01f && Mathf.Abs(first.g-settled.g)<.01f,
                    "first and settled native menu pixels agree through repeated opening");

                // Refused release: the live target is still in the world host.
                menu.enabled=false;CanvasConversion.RestoreMenuMode(panel);
                Check(panel.NativeMenuCanvas==menu && menu.renderMode==RenderMode.WorldSpace,
                    "refused native home retains the original menu canvas-mode receipt");
                // Actual release restores the original parent/camera before the mode.
                target.SetParent(null,false);menu.worldCamera=native;CanvasConversion.RestoreMenuMode(panel);
                Check(panel.NativeMenuCanvas==null && menu.renderMode==RenderMode.ScreenSpaceCamera && menu.worldCamera==native,
                    "menu release restores the original camera adapter after native camera ownership");
                menu.enabled=true;NativeLayout(scaler);native.Render();
                Check(Vector2.Distance(target.rect.size,new Vector2(1920,1080))<.02f,"native desktop home recovers its original rectangular pixel frame");
            }
            // A later attachment failure restores the complete frame through the
            // same release body; pre-return transaction faults run in rollback.
            CanvasConversion.MenuFrame(panel,target,host,new Vector2(1920,1080),true);
            target.SetParent(null,false);menu.worldCamera=native;CanvasConversion.RestoreMenuMode(panel);
            Check(panel.NativeMenuCanvas==null && menu.renderMode==RenderMode.ScreenSpaceCamera,
                "failed menu conversion restores its original canvas-mode receipt");

            CanvasConversion.MenuFrame(panel,target,host,new Vector2(1920,1080),false);
            Check(panel.NativeMenuCanvas==null,
                "ordinary converted windows retain their original canvas-mode policy");
            target.SetParent(null,false);menu.worldCamera=native;menu.renderMode=RenderMode.WorldSpace;
            CanvasConversion.MenuFrame(panel,target,host,new Vector2(1920,1080),true);
            Check(panel.NativeMenuCanvas==null,
                "already world-space menu canvases retain their original mode ownership");

            target.SetParent(null,false);menu.renderMode=RenderMode.ScreenSpaceCamera;menu.worldCamera=native;
            RectTransform nested=Rect("NativeMenuFixture.NestedCanvas",target);
            Canvas nestedCanvas=nested.gameObject.AddComponent<Canvas>();nestedCanvas.enabled=true;
            var childPanel=new ConvertedPanel {Target=nested,HostRect=host,HostGo=host.gameObject,OriginalParent=target};
            Check(!nestedCanvas.isRootCanvas && nestedCanvas.renderMode==RenderMode.ScreenSpaceCamera,
                "nested native control starts in its existing ancestor camera domain");
            CanvasConversion.MenuFrame(childPanel,nested,host,new Vector2(1920,1080),true);
            Check(childPanel.NativeMenuCanvas==null && menu.renderMode==RenderMode.ScreenSpaceCamera,
                "nested native menu canvases never rewrite their original ancestor mode");
            UnityEngine.Object.DestroyImmediate(nested.gameObject);
        }
        finally
        {
            native.targetTexture=null;head.targetTexture=null;
            UnityEngine.Object.DestroyImmediate(target.gameObject);UnityEngine.Object.DestroyImmediate(host.gameObject);
            UnityEngine.Object.DestroyImmediate(native.gameObject);UnityEngine.Object.DestroyImmediate(head.gameObject);
            nativeTarget.Release();eye.Release();UnityEngine.Object.DestroyImmediate(nativeTarget);UnityEngine.Object.DestroyImmediate(eye);
        }
        return count;
    }
}
