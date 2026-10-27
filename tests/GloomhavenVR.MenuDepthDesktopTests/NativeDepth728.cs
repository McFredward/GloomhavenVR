using System;
using System.Collections;
using System.IO;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static partial class MenuDepthDesktopProgram
{
    public static IEnumerator NativeDepthProbe728(string output, Action<int> complete)
    {
        _count = 0;
        var cameraObject = new GameObject("native orthographic depth probe");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false; camera.allowHDR=false; camera.allowMSAA=false; camera.orthographic = true; camera.orthographicSize = 96;
        camera.nearClipPlane = .1f; camera.farClipPlane = 1000; camera.cullingMask = 1 << 5;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
        var target = new RenderTexture(256, 192, 24); target.Create(); camera.targetTexture = target;
        var canvasObject = new GameObject("original screen canvas", typeof(RectTransform), typeof(Canvas));
        canvasObject.layer = 5;
        Canvas canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera; canvas.planeDistance = 100;
        var imageObject = new GameObject("original late native UI", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        imageObject.layer = 5; imageObject.transform.SetParent(canvas.transform, false);
        Image image = imageObject.GetComponent<Image>(); image.color = Color.blue;
        image.rectTransform.sizeDelta = new Vector2(100, 100);
        Canvas.ForceUpdateCanvases();
        var front = GameObject.CreatePrimitive(PrimitiveType.Cube); front.layer = 5;
        front.transform.position = image.transform.TransformPoint(new Vector3(0, 0, -10));
        front.transform.localScale = new Vector3(70, 70, 1);
        Material model = new Material(Shader.Find("Unlit/Color")); model.color = Color.red;
        front.GetComponent<MeshRenderer>().sharedMaterial = model;
        Material? changed = null;
        try
        {
            camera.Render(); Texture2D original = Read(target, Path.Combine(output, "native-always.png"));
            Check(original.GetPixel(128, 96).r > .8f, "native screen-space-camera UI already respects foreground depth by default");
            Object.DestroyImmediate(original);
            changed = new Material(image.material); changed.SetInt("unity_GUIZTestMode", 8); image.material = changed;
            Canvas.ForceUpdateCanvases(); camera.Render();
            Texture2D local = Read(target, Path.Combine(output, "material-depth.png"));
            Color at = local.GetPixel(128, 96);
            File.WriteAllText(Path.Combine(output, "probe.txt"), "localMaterialDepth=" + at + "; hasProperty=" + changed.HasProperty("unity_GUIZTestMode") + "\n");
            Object.DestroyImmediate(local);
            Check(at.b > .8f && at.r < .1f, "an explicit material Always state can reproduce the foreground overwrite independently of Canvas mode");
            changed.SetInt("unity_GUIZTestMode", 4); image.SetMaterialDirty();
            Canvas.ForceUpdateCanvases(); camera.Render();
            Texture2D restored = Read(target, Path.Combine(output, "material-less-equal.png"));
            Check(restored.GetPixel(128, 96).r > .8f, "material depth normalization is possible without native Canvas mode or transform changes");
            Object.DestroyImmediate(restored);

            // A UI camera is only a capture projector: its original near plane
            // must not clip a real raised card which is in front of that projector.
            camera.orthographicSize = 450; canvas.planeDistance = 7;
            // Native ScreenSpaceCamera TRS is applied at its original camera render,
            // after the authored projection/plane change. Establish that ordinary
            // state before placing the independent foreground witness.
            Canvas.ForceUpdateCanvases(); camera.Render();
            Canvas.ForceUpdateCanvases();
            front.transform.position = image.transform.TransformPoint(new Vector3(0, 0, -10));
            front.transform.localScale = new Vector3(70, 70, 1);
            Matrix4x4 prior = camera.projectionMatrix;
            Vector3 originalPosition = canvas.transform.position, originalScale = canvas.transform.localScale;
            RenderTexture previousTarget = RenderTexture.active;
            RenderTexture.active = target; GL.Clear(true, true, Color.black); RenderTexture.active = previousTarget;
            using (var scope = new MenuDepthNativeUiScope(camera, canvas))
            {
                File.WriteAllText(Path.Combine(output, "scope-geometry.txt"), "before: canvas=" + canvas.transform.localToWorldMatrix
                    + "; image=" + image.rectTransform.rect + "; imageWorld=" + image.transform.localToWorldMatrix
                    + "; cube=" + front.transform.position + "; projected=" + camera.WorldToViewportPoint(front.transform.position)
                    + "; clip=" + camera.projectionMatrix * camera.worldToCameraMatrix * new Vector4(front.transform.position.x, front.transform.position.y, front.transform.position.z, 1f)
                    + "; P=" + camera.projectionMatrix + "\n");
                camera.Render();
                File.AppendAllText(Path.Combine(output, "scope-geometry.txt"), "after: canvas=" + canvas.transform.localToWorldMatrix
                    + "; image=" + image.rectTransform.rect + "; imageWorld=" + image.transform.localToWorldMatrix
                    + "; projected=" + camera.WorldToViewportPoint(front.transform.position) + "\n");
                Texture2D clipped = Read(target, Path.Combine(output, "normalized-near-plane.png"));
                Check(clipped.GetPixel(128, 96).r > .8f,
                    "common UI depth retains foreground geometry in front of the original capture camera near plane");
                Object.DestroyImmediate(clipped);
                Check(canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.transform.position == originalPosition
                    && canvas.transform.localScale == originalScale,
                    "common depth leaves the native Canvas mode and world pose untouched");
            }
            Check(Same(camera.projectionMatrix, prior) && camera.clearFlags == CameraClearFlags.SolidColor,
                "native depth scope restores automatic projection and original camera clears");
            Vector3 priorCube = front.transform.position;
            // The same original cube is behind glass for a rear viewer. A front
            // witness must lose to the plane, then a rear witness must win.
            using (var scope = new MenuDepthNativeUiScope(camera, canvas, true))
            {
                RenderTexture.active = target; GL.Clear(true,true,Color.black); RenderTexture.active = previousTarget;
                camera.Render();
                Texture2D rear = Read(target, Path.Combine(output,"rear-front-witness.png"));
                Check(rear.GetPixel(128,96).b>.8f,"rear depth places the original front witness behind native glass"); Object.DestroyImmediate(rear);
                front.transform.position=image.transform.TransformPoint(new Vector3(0,0,10));
                RenderTexture.active=target; GL.Clear(true,true,Color.black); RenderTexture.active=previousTarget;
                camera.Render();
                rear=Read(target,Path.Combine(output,"rear-back-witness.png"));
                Check(rear.GetPixel(128,96).r>.8f,"rear depth exposes the original rear witness in front of native glass"); Object.DestroyImmediate(rear);
                Check(canvas.renderMode==RenderMode.ScreenSpaceCamera&&canvas.transform.position==originalPosition&&canvas.transform.localScale==originalScale,
                    "rear Z reflection retains exact original ScreenSpaceCamera geometry");
            }
            front.transform.position=priorCube;
            float priorNear=camera.nearClipPlane,priorFar=camera.farClipPlane;
            Matrix4x4 priorView=camera.worldToCameraMatrix,priorCulling=camera.cullingMatrix;
            try { using(var scope=new MenuDepthNativeUiScope(camera,canvas,true))throw new InvalidOperationException("intentional scoped render fault"); }
            catch(InvalidOperationException) { }
            Check(camera.nearClipPlane==priorNear&&camera.farClipPlane==priorFar&&Same(camera.worldToCameraMatrix,priorView)
                &&Same(camera.cullingMatrix,priorCulling)&&Same(camera.projectionMatrix,prior),"an interrupted native depth lease restores exact clip, view, culling and projection state");
            camera.transform.position=Vector3.right;
            Check(!Same(camera.worldToCameraMatrix,priorView),"restored automatic native view continues following its original camera transform");camera.transform.position=Vector3.zero;
            var secondObject=new GameObject("second native UI depth domain");var second=secondObject.AddComponent<Camera>();
            second.enabled=false;second.allowHDR=false;second.allowMSAA=false;second.orthographic=true;second.orthographicSize=450;second.nearClipPlane=.1f;second.farClipPlane=50000;
            second.targetTexture=target;second.cullingMask=1<<6;second.clearFlags=CameraClearFlags.Depth;
            var secondRoot=new GameObject("second original plane",typeof(RectTransform),typeof(Canvas));secondRoot.layer=6;
            var secondCanvas=secondRoot.GetComponent<Canvas>();secondCanvas.renderMode=RenderMode.ScreenSpaceCamera;secondCanvas.worldCamera=second;secondCanvas.planeDistance=101;
            var secondInk=new GameObject("late native plane ink",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));secondInk.layer=6;secondInk.transform.SetParent(secondRoot.transform,false);
            secondInk.GetComponent<Image>().color=Color.blue;((RectTransform)secondInk.transform).sizeDelta=new Vector2(100,100);
            Canvas.ForceUpdateCanvases();second.Render();Canvas.ForceUpdateCanvases();
            Vector3 secondPosition=secondRoot.transform.position,secondScale=secondRoot.transform.localScale;
            RenderTexture.active=target;GL.Clear(true,true,Color.black);RenderTexture.active=previousTarget;
            using(var scope=new MenuDepthNativeUiScope(camera,canvas))camera.Render();
            using(var scope=new MenuDepthNativeUiScope(second,secondCanvas))second.Render();
            Texture2D combined=Read(target,Path.Combine(output,"multiple-native-depth-domains.png"));
            Check(combined.GetPixel(128,96).r>.8f,"later UI cameras share foreground depth instead of clearing or reinterpreting the first native camera");Object.DestroyImmediate(combined);
            Check(secondRoot.transform.position==secondPosition&&secondRoot.transform.localScale==secondScale&&secondCanvas.renderMode==RenderMode.ScreenSpaceCamera,
                "different native plane distances keep each original Canvas basis");
            second.gameObject.AddComponent<MenuDepthAnchorsImageEffect724>();
            RenderTexture.active=target;GL.Clear(true,true,Color.black);RenderTexture.active=previousTarget;
            using(var scope=new MenuDepthNativeUiScope(camera,canvas))camera.Render();
            using(var scope=new MenuDepthNativeUiScope(second,secondCanvas))second.Render();
            combined=Read(target,Path.Combine(output,"multiple-native-depth-with-image-effect.png"));
            Check(combined.GetPixel(128,96).r>.8f&&second.GetComponent<MenuDepthAnchorsImageEffect724>().Draws>0,
                "the later native camera retains its actual image effect and existing foreground depth");Object.DestroyImmediate(combined);
            Object.DestroyImmediate(secondRoot);Object.DestroyImmediate(secondObject);
        }
        finally
        {
            Object.DestroyImmediate(front); Object.DestroyImmediate(canvasObject); Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(model); if (changed != null) Object.DestroyImmediate(changed);
            target.Release(); Object.DestroyImmediate(target);
        }
        complete(_count); yield break;
    }
}
