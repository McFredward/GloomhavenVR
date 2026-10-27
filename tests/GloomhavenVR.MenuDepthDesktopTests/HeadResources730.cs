using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GloomhavenVR.Rig;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static partial class MenuDepthDesktopProgram
{
    private sealed class HeadAnnotation730 : IMenuDepthSource
    {
        public Graphic Graphic { get; }
        public bool IsReady => Graphic != null && Graphic.isActiveAndEnabled;
        internal HeadAnnotation730(Graphic graphic) => Graphic = graphic;
        public void BeforeCapture(MenuDepthEye eye) { }
        public void AfterCapture() { }
    }
    private static long Ledger730() => (long)typeof(PanelSupersample).GetField("_menuDepthVramTotal",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
    private static int ScratchCount730() => ((System.Collections.IDictionary)typeof(MenuDepthCharacterSource).GetField("HeadScratchReservations",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!).Count;
    public static IEnumerator HeadResources730(string output,Action<int> complete)
    {
        _count=0;DesktopEyeInput.Available=true;
        var native=new RenderTextureDescriptor(4040,4040,RenderTextureFormat.ARGB32,24)
            {msaaSamples=4,useMipMap=true,autoGenerateMips=true,bindMS=true,useDynamicScale=true};
        Vector2Int hardware=new(4040,4040);
        RenderTextureDescriptor normalized=MenuDepthHeadRaster.Descriptor(native,hardware);
        Check(normalized.msaaSamples==1&&!normalized.bindMS&&!normalized.useMipMap&&!normalized.useDynamicScale,
            "head raster normalization removes inherited MSAA mips and dynamic scaling");
        long one=4040L*4040*8;
        Check(MenuDepthHeadRaster.SingleBytes(native,hardware)==one&&MenuDepthHeadRaster.Bytes(native,hardware)==one*2,
            "head raster ledger includes exact normalized colour and depth bytes");
        Check(MenuDepthHeadRaster.WindowBudget(native,hardware)==one*2*MenuDepthHeadRaster.WindowSlots,
            "eight hardware-sized pairs have a finite XR-derived window ceiling");
        Check(MenuDepthHeadRaster.SceneBudget(native,hardware)==one*MenuDepthHeadRaster.SceneSlots,
            "four hardware-sized native scene scratches have a finite separate ceiling");
        File.WriteAllText(Path.Combine(output,"4040-resource-budget.txt"),"single="+one+";pair="+one*2+";eightPairs="+one*16+";fourScratches="+one*4+";ordinaryMonoBudget="+448L*1024*1024+"\n");
        using(var scene=new Scene())
        {
            scene.HideSpatial730();WorldUIConfig.CharacterPreviewDepth.Value=false;MenuDepthCharacters.Tick();
            scene.HeadTarget.Release();scene.HeadTarget.width=64;scene.HeadTarget.height=64;scene.HeadTarget.Create();
            scene.Head.orthographic=false;scene.Head.fieldOfView=70;scene.Eyes(Vector3.zero);
            var entries=new List<PanelSupersample.Entry>();var sources=new List<HeadAnnotation730>();
            var targets=new List<RenderTexture>();var objects=new List<GameObject>();
            try
            {
                for(int i=0;i<9;i++)
                {
                    var root=new GameObject("original fixed window "+i,typeof(RectTransform),typeof(Canvas));objects.Add(root);root.layer=8;
                    var rect=(RectTransform)root.transform;rect.sizeDelta=new Vector2(200,200);rect.position=new Vector3(0,0,2);rect.localScale=Vector3.one*.005f;
                    root.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;
                    var image=new GameObject("original visible annotation child",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));image.layer=8;image.transform.SetParent(root.transform,false);
                    image.GetComponent<Image>().rectTransform.sizeDelta=new Vector2(200,200);
                    var annotation=new HeadAnnotation730(image.GetComponent<Image>());sources.Add(annotation);MenuDepthSources.Register(annotation);
                    var capture=new GameObject("original UI capture "+i).AddComponent<Camera>();objects.Add(capture.gameObject);capture.enabled=false;
                    capture.orthographic=true;capture.orthographicSize=.5f;capture.transform.position=Vector3.zero;capture.cullingMask=1<<8;capture.clearFlags=CameraClearFlags.SolidColor;
                    var rt=new RenderTexture(64,64,24,RenderTextureFormat.ARGB32);rt.Create();targets.Add(rt);capture.targetTexture=rt;
                    var display=new GameObject("original native display "+i,typeof(RectTransform),typeof(Canvas),typeof(CanvasRenderer),typeof(RawImage));objects.Add(display);display.layer=27;
                    display.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;var dr=(RectTransform)display.transform;dr.sizeDelta=rect.sizeDelta;dr.position=rect.position;dr.localScale=rect.localScale;
                    display.GetComponent<RawImage>().texture=rt;
                    entries.Add(PanelSupersample.BindHead(rect,capture,rt,display.GetComponent<RawImage>()));
                }
                Canvas.ForceUpdateCanvases();
                Check(entries.TrueForAll(e=>MenuDepthSources.HasContent(e.Panel.HostRect)),"each original annotation child admits its native fixed window");
                int initial=Time.frameCount;while(Time.frameCount==initial)yield return null;scene.Head.Render();
                int admitted=0,refused=0;foreach(var entry in entries){if(entry.MenuDepthDisplay!=null)admitted++;if(entry.MenuDepthFailures>0)refused++;}
                File.WriteAllText(Path.Combine(output,"window-admission.txt"),"admitted="+admitted+";refused="+refused+";ledger="+Ledger730()+"\n"+string.Join("\n",GloomhavenVR.Core.VRLog.Messages));
                Check(admitted==8&&refused==1,"the ninth real window is refused before allocating a ninth head pair");
                Check(Ledger730()==64L*64*8*2*8,"all eight actual window pairs are charged independently of ordinary mono captures");
                PanelSupersample.Entry retired=entries.Find(e=>e.MenuDepthDisplay!=null)!;PanelSupersample.EndHead(retired);entries.Remove(retired);
                int before=Time.frameCount;while(Time.frameCount==before)yield return null;scene.Head.Render();
                Check(entries.TrueForAll(e=>e.MenuDepthDisplay!=null),"closing an admitted native window immediately makes its head slot reusable");
                scene.HeadTarget.Release();scene.HeadTarget.width=128;scene.HeadTarget.height=96;scene.HeadTarget.Create();
                before=Time.frameCount;while(Time.frameCount==before)yield return null;scene.Head.Render();
                Check(Ledger730()==128L*96*8*2*8,"XR resize updates every real head-pair ledger entry without double charging");
                foreach(var entry in entries)
                {
                    entry.Rt.Release();entry.Rt.format=RenderTextureFormat.ARGBHalf;entry.Rt.Create();
                }
                before=Time.frameCount;while(Time.frameCount==before)yield return null;scene.Head.Render();
                Check(Ledger730()==128L*96*12*2*8,"native format replacement updates actual head colour and depth charges");
            }
            finally
            {
                foreach(var entry in entries)PanelSupersample.EndHead(entry);
                foreach(var source in sources)MenuDepthSources.Unregister(source);
                foreach(var target in targets){target.Release();Object.DestroyImmediate(target);}
                foreach(var go in objects)Object.DestroyImmediate(go);
                WorldUIConfig.CharacterPreviewDepth.Value=true;
            }
            Check(Ledger730()==0,"closing all native windows retires every head-pair reservation");
        }
        using(var scene=new Scene())
        {
            scene.HideSpatial730();MenuDepthCharacters.Shutdown();
            scene.HeadTarget.Release();scene.HeadTarget.width=128;scene.HeadTarget.height=96;scene.HeadTarget.Create();
            var manager=scene.ModelCamera.GetComponent<Character3DDisplayManager>();
            var sources=new List<MenuDepthCharacterSource>();var images=new List<GameObject>();var held=new List<RenderTexture>();
            try
            {
                for(int i=0;i<5;i++)
                {
                    var go=new GameObject("original preview consumer "+i,typeof(RectTransform),typeof(CanvasRenderer),typeof(RawImage));images.Add(go);go.transform.SetParent(scene.RootA,false);
                    var image=go.GetComponent<RawImage>();image.texture=scene.ModelTarget;image.rectTransform.sizeDelta=new Vector2(100,100);
                    sources.Add(new MenuDepthCharacterSource(image,scene.ModelCamera,manager,manager.Holder,m=>m.Hidden));
                }
                Canvas.ForceUpdateCanvases();
                for(int i=0;i<4;i++)
                {
                    sources[i].PrimeDirectCapture(new MenuDepthEye(false,Vector3.zero,scene.Head));
                    var target=sources[i].DepthTexture(false)!;held.Add(target);
                    Check(MenuDepthHeadRaster.Matches(target,scene.ModelTarget.descriptor,new Vector2Int(128,96)),"actual native scene scratch matches the normalized head descriptor");
                    sources[i].AfterCapture();
                }
                bool refused=false;try{sources[4].PrimeDirectCapture(new MenuDepthEye(false,Vector3.zero,scene.Head));}catch(InvalidOperationException){refused=true;}
                Check(refused&&ScratchCount730()==4,"the fifth real native preview cannot allocate a fifth head scratch");
                foreach(var source in sources)source.MaintainResources(Time.unscaledTime+3);
                Check(ScratchCount730()==0&&held.TrueForAll(t=>!t.IsCreated()),"idle original previews release all native scene scratch reservations and surfaces");
            }
            finally{foreach(var source in sources)source.Dispose();foreach(var go in images)Object.DestroyImmediate(go);}
        }
        complete(_count);
    }
}
