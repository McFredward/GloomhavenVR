using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Core;
using GloomhavenVR.Rig;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Camera poses and native model/mono-output state are explicit inputs. Canvas
// priming, head Camera.onPreCull/onPostRender, native source renders, real masks
// and the next Unity frame's cleanup callback are production/actual Unity.
public static class MenuDepthCharacterDirectProof
{
    private static int _checks;
    private static void Check(bool value, string message) { _checks++; if (!value) throw new Exception(message); }
    public static IEnumerator AutomaticHead(string output) => Run(output, false);
    public static IEnumerator AutomaticHeadFailure(string output) => Run(output, true);
    private static IEnumerator Run(string output, bool failure)
    {
        _checks = 0; VRSession.IsRunning = true;
        using (var scene = new MenuDepthCharacterPresentation725.Scene(output, true, seed: false))
        {
            scene.SetFrequency(0);
            MenuDepthCharacterSource? second = null;
            try
            {
                scene.Root.position = new Vector3(-.7f, 0, 1.5f); scene.Root.rotation = Quaternion.Euler(0, 12, 0);
                var firstWindow = new Window(scene.Root, scene.Image, scene.Source);
                var secondGo = new GameObject("second original native preview window", typeof(RectTransform), typeof(Canvas)); secondGo.layer = 5;
                var secondRoot = (RectTransform)secondGo.transform; secondRoot.sizeDelta = new Vector2(1100,800); secondRoot.localScale = Vector3.one * .001f;
                secondRoot.position = new Vector3(.7f, .1f, 1.5f); secondRoot.rotation = Quaternion.Euler(0,-18,0);
                secondGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                var secondImageGo = new GameObject("second original consumer", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage)); secondImageGo.layer = 5;
                secondImageGo.transform.SetParent(secondRoot, false); RawImage secondImage = secondImageGo.GetComponent<RawImage>();
                secondImage.rectTransform.sizeDelta = new Vector2(1100,800); secondImage.texture = scene.Native; secondImage.uvRect = new Rect(.05f,.1f,.8f,.8f);
                second = new MenuDepthCharacterSource(secondImage, scene.NativeCamera, scene.Manager, scene.Holder, m => m.Hidden);
                MenuDepthSources.Register(second);
                var secondWindow = new Window(secondRoot, secondImage, second);
                AddMask(firstWindow, true); AddMask(secondWindow, false);
                scene.Head.enabled = true; scene.Head.transform.SetPositionAndRotation(new Vector3(0,0,-.3f), Quaternion.identity);
                VRRigDriver.HeadCamera = scene.Head;
                FillMono(scene.Native);
                int before = scene.Effect!.Draws;
                Canvas.ForceUpdateCanvases();
                Check(scene.Effect.Draws == before, "actual early Canvas priming never renders or advances the native character scene");
                bool loaned = !ReferenceEquals(scene.Image.texture, scene.Native) && !ReferenceEquals(secondImage.texture, scene.Native);
                RenderTexture leftA = (RenderTexture)scene.Image.texture, leftB = (RenderTexture)secondImage.texture;
                var rendered = new List<RenderTexture>(); scene.Effect.BeforeBlit += () => rendered.Add(scene.NativeCamera.targetTexture);
                for (int eye = 0; eye < 2; eye++)
                {
                    scene.Head.transform.position = new Vector3(eye == 0 ? -.032f : .032f, .04f, -.3f);
                    scene.Head.Render();
                    Check(ReferenceEquals(scene.Image.texture,scene.Native) && ReferenceEquals(secondImage.texture,scene.Native),
                        "automatic head completion restores native properties for every prepared or failed-prime consumer");
                    Check(ReferenceEquals(scene.NativeCamera.targetTexture,scene.Native) && scene.Native.IsCreated(),
                        "automatic head capture preserves the exact original native camera target");
                    Texture2D pixels = Read(scene.Final,Path.Combine(output,"automatic-head-"+eye+".png"));
                    if (failure)
                    {
                        CheckFallback(pixels,scene.Head,firstWindow); CheckFallback(pixels,scene.Head,secondWindow);
                    }
                    else
                    {
                        CheckWindow(pixels,scene.Head,firstWindow,0); CheckWindow(pixels,scene.Head,secondWindow,1);
                        Check(scene.Invariant,"actual direct rendering keeps the original camera-child models lights and animation fixed");
                    }
                    Object.DestroyImmediate(pixels);
                }
                Check(loaned,"actual Canvas callback loans each consumer its stable batch texture before the first head draw");
                Check(!ReferenceEquals(leftA,leftB), "differently placed original consumers retain separate native eye surfaces");
                Check((leftA.width * (long)leftA.height + leftB.width * (long)leftB.height) * 16 <= 96L*1024*1024,
                    "automatic direct consumers retain the shared96MiB eye-pair reservation cap");
                if (failure)
                    Check(VRLog.Messages.Exists(s=>s.Contains("injected automatic head failure")),
                        "automatic failure remains visible in bounded native capture diagnostics");
                else
                    Check(rendered.Count == 4 && ReferenceEquals(rendered[0],leftA) && ReferenceEquals(rendered[1],leftB)
                        && ReferenceEquals(rendered[2],leftA) && ReferenceEquals(rendered[3],leftB),
                        "both actual tracked-position draws refresh the same per-consumer batch surfaces with their own views");

                // Deliberately skip the head draw after a genuine Canvas prime.
                // Production's real next-frame Update/EndOfFrame must recover it.
                scene.Head.transform.position = new Vector3(.12f,.06f,-.25f);
                Canvas.ForceUpdateCanvases();
                Check(!ReferenceEquals(scene.Image.texture,scene.Native),"missing-head control starts with a real outstanding Canvas prime");
                scene.Head.enabled = false; VRRigDriver.HeadCamera = null;
                int frame = Time.frameCount; float timeout = Time.realtimeSinceStartup + 10f;
                while (Time.frameCount < frame + 2)
                {
                    Check(Time.realtimeSinceStartup < timeout,"actual Unity frames advance for automatic prime cleanup");
                    yield return null;
                }
                Check(ReferenceEquals(scene.Image.texture,scene.Native) && ReferenceEquals(secondImage.texture,scene.Native),
                    "actual next Unity frame restores orphaned texture primes when no head draw occurs");
                Check(scene.Native.IsCreated(),"missing-head cleanup never releases the native game-owned target");

                // A depth-feature dismissal keeps the original RawImage visible.
                // Its already-built native Canvas batch must receive mono pixels
                // before the primed source's owned target can be retired.
                scene.Head.enabled = true; VRRigDriver.HeadCamera = scene.Head;
                FillMono(scene.Native); Canvas.ForceUpdateCanvases(); MenuDepthSources.Unregister(second); second.Dispose(); second = null;
                Check(ReferenceEquals(secondImage.texture,scene.Native) && !MenuDepthSources.HasContent(secondRoot),
                    "unregistering a dismissed primed consumer restores its native texture and exact membership");
                scene.Head.Render();
                Check(ReferenceEquals(scene.Image.texture,scene.Native) && ReferenceEquals(scene.NativeCamera.targetTexture,scene.Native),
                    "remaining automatic consumer continues with the original camera and texture ownership");
                Texture2D retired = Read(scene.Final,Path.Combine(output,"automatic-visible-unregister.png"));
                CheckFallback(retired,scene.Head,secondWindow); Object.DestroyImmediate(retired);
                if (!failure)
                {
                    // An explicit fault-only source supplies no rendering. The
                    // actual public Canvas callback and production recovery must
                    // contain getter/prime/restore exceptions around healthy UI.
                    var fault = new FaultSource(scene.Image) { Outside = true };
                    MenuDepthSources.Register(fault); Canvas.ForceUpdateCanvases(); MenuDepthSources.Unregister(fault);
                    Check(VRLog.Messages.Exists(s=>s.StartsWith("MENU DEPTH DIRECT BATCH:",StringComparison.Ordinal)&&s.Contains("injected external Canvas fault")),
                        "external Canvas candidate failure is contained and reported by the actual callback");
                    fault.Outside = false; MenuDepthSources.Register(fault);
                    for(int attempt=0;attempt<8;attempt++) Canvas.ForceUpdateCanvases();
                    MenuDepthSources.Unregister(fault);
                    Check(fault.PrimeCalls==8&&fault.RestoreCalls==8,"actual Canvas prime failures attempt safe restoration without aborting healthy sources");
                    Check(VRLog.Messages.FindAll(s=>s.StartsWith("MENU DEPTH SOURCE RESTORE:",StringComparison.Ordinal)).Count==5,
                        "repeated automatic restore failures retain bounded useful normal diagnostics");
                    Canvas.ForceUpdateCanvases(); scene.Head.Render();
                    Texture2D healthy=Read(scene.Final,Path.Combine(output,"automatic-after-external-faults.png"));
                    CheckWindow(healthy,scene.Head,firstWindow,0); Object.DestroyImmediate(healthy);
                }
            }
            finally
            {
                VRRigDriver.HeadCamera = null;
                if (second != null) { MenuDepthSources.Unregister(second); second.Dispose(); }
            }
        }
        yield return _checks;
    }
    private static void AddMask(Window window, bool stencil)
    {
        CanvasGroup group = window.Root.gameObject.AddComponent<CanvasGroup>(); group.alpha = .7f;
        window.Image.color = new Color(1,1,1,.65f);
        var mask = new GameObject(stencil ? "original stencil mask" : "original rectangular mask", typeof(RectTransform)); mask.layer = 5;
        mask.transform.SetParent(window.Root,false); var rect = (RectTransform)mask.transform;
        rect.sizeDelta = new Vector2(window.Root.rect.width*.9f,window.Root.rect.height*.6f);
        if (stencil) { mask.AddComponent<CanvasRenderer>(); mask.AddComponent<Image>(); mask.AddComponent<Mask>().showMaskGraphic = false; }
        else mask.AddComponent<RectMask2D>();
        window.Image.transform.SetParent(rect,false);
    }
    private static void FillMono(RenderTexture texture)
    {
        RenderTexture old = RenderTexture.active; RenderTexture.active = texture; GL.Clear(true,true,Color.magenta); RenderTexture.active = old;
    }
    private static Texture2D Read(RenderTexture target,string path)
    {
        RenderTexture old=RenderTexture.active;RenderTexture.active=target;
        var pixels=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
        pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();RenderTexture.active=old;
        File.WriteAllBytes(path,pixels.EncodeToPNG());return pixels;
    }
    private static Vector2 Project(Camera head,Vector3 point)
    {
        Vector3 p=Quaternion.Inverse(head.transform.rotation)*(point-head.transform.position);
        float f=1/Mathf.Tan(30*Mathf.Deg2Rad);
        return new Vector2(.5f+p.x/p.z*f/head.aspect*.5f,.5f+p.y/p.z*f*.5f);
    }
    private static Vector2 Centroid(Texture2D image,int half,int channel,out int peak)
    {
        Color32[] pixels=image.GetPixels32();double weight=0,x=0,y=0;peak=0;
        int start=half==0?0:image.width/2,end=half==0?image.width/2:image.width;
        for(int iy=0;iy<image.height;iy++)for(int ix=start;ix<end;ix++)
        {
            Color32 p=pixels[iy*image.width+ix];int own=channel==0?p.r:p.g,other=channel==0?Math.Max(p.g,p.b):Math.Max(p.r,p.b);
            if(own<20||own<other*2)continue;peak=Math.Max(peak,own);weight+=own;x+=(ix+.5)*own;y+=(iy+.5)*own;
        }
        Check(weight>255*5,"automatic head final raster contains both native preview landmarks");
        return new Vector2((float)(x/weight/image.width),(float)(y/weight/image.height));
    }
    private static void CheckWindow(Texture2D pixels,Camera head,Window window,int half)
    {
        var native=new[]{new Vector3(.4f,-.1f,2.3f),new Vector3(1.2f,.5f,2.9f)};
        for(int i=0;i<2;i++)
        {
            Vector2 observed=Centroid(pixels,half,i,out int peak),expected=Project(head,window.Map(native[i]));
            float error=Vector2.Scale(observed-expected,new Vector2(pixels.width,pixels.height)).magnitude;
            Check(error<1.5f,"automatic final head landmarks match each differently placed fixed native cabinet");
            Check(Mathf.Abs(peak/255f-.455f)<.025f,"automatic native UI retains its original colour alpha and ancestor fade");
        }
        Vector2 clipped=Project(head,window.Map(new Vector3(.4f,.72f,2.56f)));
        Color sample=pixels.GetPixel((int)(clipped.x*pixels.width),(int)(clipped.y*pixels.height));
        Check(Mathf.Max(sample.r,Mathf.Max(sample.g,sample.b))<.04f,"automatic native stencil and rectangle masks clip the original preview");
    }
    private static void CheckFallback(Texture2D pixels,Camera head,Window window)
    {
        Vector2 center=Project(head,window.Root.position);Color p=pixels.GetPixel((int)(center.x*pixels.width),(int)(center.y*pixels.height));
        Check(Mathf.Abs(p.r-.455f)<.025f&&p.g<.025f&&Mathf.Abs(p.b-.455f)<.025f,
            "failing automatic head uses native mono colour through the already-built stable Canvas sampler");
    }
    private sealed class Window
    {
        internal readonly RectTransform Root;internal readonly RawImage Image;
        internal Window(RectTransform root,RawImage image,MenuDepthCharacterSource source){Root=root;Image=image;}
        internal Vector3 Map(Vector3 native)
        {
            float w=Image.rectTransform.rect.width*Image.rectTransform.TransformVector(Vector3.right).magnitude;
            float h=Image.rectTransform.rect.height*Image.rectTransform.TransformVector(Vector3.up).magnitude;
            float n=Mathf.Max(1.29f/(w*.82f),1.8f/(h*.75f)),z=Mathf.Max(n,.6f/(Mathf.Min(w,h)*.14f));
            float plane=2.3f+Mathf.Min(Mathf.Min(w,h)*.035f,.6f/z*.30f)*z;
            Vector3 d=native-new Vector3(.595f,-.1f,plane);
            return Root.position+Root.right*d.x/n+Root.up*d.y/n+Root.forward*d.z/z;
        }
    }
    private sealed class FaultSource : IMenuDepthDirectSource
    {
        private readonly Graphic _graphic;
        internal bool Outside;
        internal int PrimeCalls,RestoreCalls;
        internal FaultSource(Graphic graphic){_graphic=graphic;}
        public Graphic Graphic => Outside ? throw new InvalidOperationException("injected external Canvas fault") : _graphic;
        public bool IsReady => true;
        public void PrimeDirectCapture(MenuDepthEye eye){PrimeCalls++;throw new InvalidOperationException("injected direct prime fault");}
        public void BeforeDirectCapture(MenuDepthEye eye)=>throw new InvalidOperationException("unregistered fault source unexpectedly reached a head draw");
        public void BeforeCapture(MenuDepthEye eye)=>BeforeDirectCapture(eye);
        public void AfterCapture(){RestoreCalls++;throw new InvalidOperationException("injected direct restore fault");}
        public void CancelDirectCapture()=>AfterCapture();
    }
}
